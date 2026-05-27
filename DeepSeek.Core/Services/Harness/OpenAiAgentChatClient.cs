using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

public sealed class OpenAiAgentChatClient : IAgentWebChat, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppConfig _config;
    private readonly HttpClient _http;

    public OpenAiAgentChatClient(AppConfig config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<WebChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null)
    {
        var apiKey = RequireApiKey();
        var body = BuildRequestBody(messages, model, thinking, search, allowToolCalls, options, stream: false);
        var url = AgentChatClientFactory.ResolveBaseUrl(_config) + "/v1/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var ex = new InvalidOperationException($"API 错误 ({(int)resp.StatusCode}): {Truncate(json, 500)}");
                AgentRequestTelemetry.TryPublish(
                    _config, model, thinking, search, null, sw.ElapsedMilliseconds, stream: false, "openai-api", ex);
                throw ex;
            }

            var result = ParseCompletion(json, model);
            AgentRequestTelemetry.TryPublish(
                _config, model, thinking, search, result, sw.ElapsedMilliseconds, stream: false, "openai-api");
            return result;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            AgentRequestTelemetry.TryPublish(
                _config, model, thinking, search, null, sw.ElapsedMilliseconds, stream: false, "openai-api", ex);
            throw;
        }
    }

    /// <summary>
    /// 真流式：逐 chunk 推送 delta（对齐 DeepSeek-TUI / deepcode-cli，避免整段缓冲 60s+ 无 UI 反馈）。
    /// </summary>
    public async IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        [EnumeratorCancellation] CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        AgentChatOptions? options = null)
    {
        var sw = Stopwatch.StartNew();
        var apiKey = RequireApiKey();
        var body = BuildRequestBody(messages, model, thinking, search, allowToolCalls, options, stream: true);
        var url = AgentChatClientFactory.ResolveBaseUrl(_config) + "/v1/chat/completions";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            var ex = new InvalidOperationException($"API 错误 ({(int)response.StatusCode}): {Truncate(err, 500)}");
            AgentRequestTelemetry.TryPublish(
                _config, model, thinking, search, null, sw.ElapsedMilliseconds, stream: true, "openai-api", ex);
            throw ex;
        }

        await using var streamBody = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(streamBody);

        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();
        var toolCalls = new Dictionary<int, (string Id, string Name, StringBuilder Args)>();
        var announcedToolIndexes = new HashSet<int>();
        string? finishReason = null;
        var resolvedModel = model;
        int promptTokens = 0, completionTokens = 0, totalTokens = 0;
        var chunkTimeout = TimeSpan.FromSeconds(120);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await ReadSseLineAsync(reader, chunkTimeout, ct);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            ParseUsage(root, ref promptTokens, ref completionTokens, ref totalTokens);
            if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            {
                var m = modelEl.GetString();
                if (!string.IsNullOrWhiteSpace(m))
                    resolvedModel = m;
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                finishReason = fr.GetString();

            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            {
                var text = rc.GetString() ?? "";
                if (text.Length > 0)
                {
                    reasoningBuilder.Append(text);
                    yield return new WebChatStreamDelta("thinking", text);
                }
            }

            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString() ?? "";
                if (text.Length > 0)
                {
                    contentBuilder.Append(text);
                    yield return new WebChatStreamDelta("content", text);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    var index = tc.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                    if (!toolCalls.TryGetValue(index, out var entry))
                    {
                        var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        var name = "";
                        if (tc.TryGetProperty("function", out var fn)
                            && fn.TryGetProperty("name", out var nameEl))
                            name = nameEl.GetString() ?? "";
                        entry = (id, name, new StringBuilder());
                        toolCalls[index] = entry;
                    }

                    if (tc.TryGetProperty("function", out var function))
                    {
                        if (function.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                            entry.Name = nEl.GetString() ?? entry.Name;
                        if (function.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.String)
                            entry.Args.Append(aEl.GetString());
                    }

                    toolCalls[index] = entry;

                    if (!string.IsNullOrWhiteSpace(entry.Name)
                        && announcedToolIndexes.Add(index))
                    {
                        yield return new WebChatStreamDelta("tool", entry.Name);
                    }
                }
            }
        }

        var result = new WebChatResult
        {
            Content = contentBuilder.Length > 0 ? contentBuilder.ToString() : null,
            ReasoningContent = reasoningBuilder.Length > 0 ? reasoningBuilder.ToString() : null,
            Model = resolvedModel,
            FinishReason = finishReason,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens > 0 ? totalTokens : promptTokens + completionTokens,
            ToolCalls = toolCalls.Count == 0
                ? null
                : toolCalls.OrderBy(kv => kv.Key).Select(kv => new WebToolCall
                {
                    Id = string.IsNullOrWhiteSpace(kv.Value.Id) ? "call_" + kv.Key : kv.Value.Id,
                    Name = kv.Value.Name,
                    Arguments = kv.Value.Args.Length > 0 ? kv.Value.Args.ToString() : "{}"
                }).ToList()
        };

        yield return new WebChatStreamDone(result);
        AgentRequestTelemetry.TryPublish(
            _config, model, thinking, search, result, sw.ElapsedMilliseconds, stream: true, "openai-api");
    }

    private string RequireApiKey()
    {
        var apiKey = AgentChatClientFactory.ResolveApiKey(_config);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Agent API 模式需要配置 API Key（设置页或 DeepSeekApiKey）。");
        return apiKey;
    }

    private object BuildRequestBody(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        bool allowToolCalls,
        AgentChatOptions? options,
        bool stream)
    {
        var apiMessages = messages.Select(m => ToApiMessage(m, _config.AgentPrefixCacheEnabled)).ToList();
        var payload = new Dictionary<string, object?>
        {
            ["model"] = string.IsNullOrWhiteSpace(model) ? _config.Model : model,
            ["messages"] = apiMessages,
            ["stream"] = stream
        };

        if (thinking)
        {
            payload["thinking"] = new { type = "enabled" };
            var effort = AgentReasoningEfforts.Normalize(options?.ReasoningEffort ?? _config.AgentReasoningEffort);
            payload["reasoning_effort"] = effort;
        }

        if (search)
            payload["web_search"] = true;

        if (allowToolCalls && options is { UseOpenAiTools: true, Tools: { Count: > 0 } tools })
            payload["tools"] = tools;

        return payload;
    }

    private static object ToApiMessage(ChatMessage message, bool prefixCache)
    {
        if (prefixCache
            && string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
            && message.ToolCalls is not { Count: > 0 }
            && message.ContentParts is not { Count: > 0 })
        {
            return new
            {
                role = "system",
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = message.Content ?? "",
                        cache_control = new { type = "ephemeral" }
                    }
                }
            };
        }

        return BuildApiMessage(message);
    }

    private static object BuildApiMessage(ChatMessage message)
    {
        if (string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                role = "tool",
                content = message.Content ?? "",
                tool_call_id = message.ToolCallId ?? ""
            };
        }

        if (message.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = "assistant",
                content = message.Content,
                tool_calls = message.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments }
                }).ToList()
            };
        }

        if (message.ContentParts is { Count: > 0 } parts)
        {
            return new
            {
                role = message.Role,
                content = parts.Select(p =>
                {
                    if (string.Equals(p.Type, "image_url", StringComparison.OrdinalIgnoreCase))
                        return (object)new { type = "image_url", image_url = new { url = p.ImageUrl ?? "" } };
                    return (object)new { type = "text", text = p.Text ?? "" };
                }).ToList()
            };
        }

        return new { role = message.Role, content = message.Content ?? "" };
    }

    private static WebChatResult ParseCompletion(string json, string model)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return new WebChatResult { Content = json, Model = model };

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var c) ? c.GetString() : null;
        var reasoning = message.TryGetProperty("reasoning_content", out var r) ? r.GetString() : null;
        var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

        List<WebToolCall>? toolCalls = null;
        if (message.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            toolCalls = new List<WebToolCall>();
            foreach (var tc in tcs.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                toolCalls.Add(new WebToolCall
                {
                    Id = tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Name = fn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Arguments = fn.TryGetProperty("arguments", out var a) ? a.GetString() ?? "{}" : "{}"
                });
            }
        }

        var promptTokens = 0;
        var completionTokens = 0;
        var totalTokens = 0;
        ParseUsage(root, ref promptTokens, ref completionTokens, ref totalTokens);

        return new WebChatResult
        {
            Content = content,
            ReasoningContent = reasoning,
            ToolCalls = toolCalls,
            Model = root.TryGetProperty("model", out var m) ? m.GetString() ?? model : model,
            FinishReason = finish,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens > 0 ? totalTokens : promptTokens + completionTokens
        };
    }

    private static void ParseUsage(JsonElement root, ref int promptTokens, ref int completionTokens, ref int totalTokens)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return;
        if (usage.TryGetProperty("prompt_tokens", out var pt))
            promptTokens = pt.GetInt32();
        if (usage.TryGetProperty("completion_tokens", out var ct))
            completionTokens = ct.GetInt32();
        if (usage.TryGetProperty("total_tokens", out var tt))
            totalTokens = tt.GetInt32();
    }

    private static async Task<string?> ReadSseLineAsync(StreamReader reader, TimeSpan chunkTimeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(chunkTimeout);
        try
        {
            return await reader.ReadLineAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("SSE chunk timeout after " + (int)chunkTimeout.TotalSeconds + "s");
        }
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    public void Dispose() => _http.Dispose();
}
