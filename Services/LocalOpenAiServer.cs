using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 可选的外部 OpenAI 兼容 HTTP API（供本机第三方工具调用）。
/// </summary>
public sealed class LocalOpenAiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly WebInjectService _web;
    private readonly Chat2ApiSessionStore _sessions = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private AppConfig _config = new();
    private int _agentScopeDepth;

    public LocalOpenAiServer(WebInjectService web) => _web = web;

    public string BaseUrl => InternalChatChannel.GetExternalApiBaseUrl(_config);

    public bool IsListening => _listener is { IsListening: true };

    /// <summary>Agent 运行期间为 TUI 子进程提供临时 loopback 转发（任务结束即停）。</summary>
    public void EnsureAgentScopedListening()
    {
        Interlocked.Increment(ref _agentScopeDepth);
        if (!IsListening)
            StartInternal(allowAgentScope: true);
    }

    public void ReleaseAgentScopedListening()
    {
        if (Interlocked.Decrement(ref _agentScopeDepth) > 0)
            return;
        if (_agentScopeDepth < 0)
            Interlocked.Exchange(ref _agentScopeDepth, 0);
        if (!_config.EnableExternalOpenAiApi)
            Stop();
    }

    public void EnsureExternalApiListening()
    {
        if (!_config.EnableExternalOpenAiApi)
            return;
        if (!IsListening)
            StartInternal(allowAgentScope: false);
    }

    public IReadOnlyList<Chat2ApiSessionInfo> ListSessions() => _sessions.ListSessions(_config);

    public bool DeleteSession(string sessionId) => _sessions.Delete(sessionId);

    public void UpdateConfig(AppConfig config)
    {
        var portChanged = InternalChatChannel.ResolveExternalApiPort(_config) !=
                          InternalChatChannel.ResolveExternalApiPort(config);
        var wasEnabled = _config.EnableExternalOpenAiApi;
        _config = config;
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        if (!config.EnableExternalOpenAiApi && _agentScopeDepth == 0)
            Stop();
        else if ((portChanged || !wasEnabled || _agentScopeDepth > 0) && _listener is { IsListening: true })
            StartInternal(allowAgentScope: _agentScopeDepth > 0 || config.EnableExternalOpenAiApi);
    }

    public void Start() => StartInternal(allowAgentScope: _agentScopeDepth > 0 || _config.EnableExternalOpenAiApi);

    private void StartInternal(bool allowAgentScope)
    {
        if (!allowAgentScope && !_config.EnableExternalOpenAiApi)
            return;
        Stop();
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        var port = InternalChatChannel.ResolveExternalApiPort(_config);
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener?.Close();
        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { IsListening: true })
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequestAsync(ctx), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                ctx.Response.Close();
                return;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                (path.Equals("/v1/health", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/health", StringComparison.OrdinalIgnoreCase)))
            {
                await WriteJsonAsync(ctx, 200, await BuildHealthPayloadAsync());
                return;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                (path.Equals("/v1/providers", StringComparison.OrdinalIgnoreCase) ||
                 path.Equals("/v1/integration", StringComparison.OrdinalIgnoreCase)))
            {
                Chat2ApiHealth? probe = null;
                try
                {
                    probe = await _web.ProbeChat2ApiHealthAsync(_config.WebUserToken, BaseUrl);
                }
                catch
                {
                    // ignore
                }

                var snap = Chat2ApiProviderService.Build(_config, probe);
                await WriteJsonAsync(ctx, 200, Chat2ApiProviderService.ToApiPayload(snap));
                return;
            }

            if (!TryAuthorize(ctx, out var authError))
            {
                await WriteJsonAsync(ctx, 401, authError!);
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path.Equals("/v1/models", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(ctx, 200, new
                {
                    @object = "list",
                    data = Chat2ApiCompat.ListModels(_config)
                });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                path.StartsWith("/v1/models/", StringComparison.OrdinalIgnoreCase))
            {
                var modelId = Uri.UnescapeDataString(path["/v1/models/".Length..]);
                var model = Chat2ApiCompat.GetModel(modelId, _config);
                if (model is null)
                {
                    await WriteJsonAsync(ctx, 404,
                        new { error = new { message = "Model not found", type = "invalid_request_error" } });
                    return;
                }

                await WriteJsonAsync(ctx, 200, model);
                return;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                path.Equals("/v1/admin/sessions", StringComparison.OrdinalIgnoreCase) &&
                LocalApiKeyService.IsLoopback(ctx.Request))
            {
                await WriteJsonAsync(ctx, 200, new
                {
                    sessions = _sessions.ListSessions(_config),
                    mode = _config.Chat2ApiSessionMode
                });
                return;
            }

            if (ctx.Request.HttpMethod == "DELETE" &&
                path.StartsWith("/v1/admin/sessions/", StringComparison.OrdinalIgnoreCase) &&
                LocalApiKeyService.IsLoopback(ctx.Request))
            {
                var sid = Uri.UnescapeDataString(path["/v1/admin/sessions/".Length..]);
                var ok = _sessions.Delete(sid);
                await WriteJsonAsync(ctx, ok ? 200 : 404, new { success = ok });
                return;
            }

            if (ctx.Request.HttpMethod == "POST" &&
                path.Equals("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var bodyText = await reader.ReadToEndAsync();
                var req = Chat2ApiCompat.ParseCompletion(bodyText, ctx.Request, _config, _sessions);

                if (req.Stream)
                {
                    await HandleChatCompletionStreamAsync(ctx, req);
                    return;
                }

                var response = await HandleChatCompletionAsync(req);
                await WriteJsonAsync(ctx, 200, response);
                return;
            }

            await WriteJsonAsync(ctx, 404, new { error = new { message = "Not found", type = "invalid_request_error" } });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(ctx, 500, new { error = new { message = ex.Message, type = "server_error" } });
        }
    }

    private async Task HandleChatCompletionStreamAsync(
        HttpListenerContext ctx,
        Chat2ApiCompat.CompletionRequest req)
    {
        try
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            var events = ExecuteChatStreamAsync(req);
            await Chat2ApiSseWriter.PipeWebStreamAsync(
                ctx.Response.OutputStream, events, req.RequestedModel, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (!ctx.Response.OutputStream.CanWrite)
                throw;
            var err = JsonSerializer.Serialize(new
            {
                error = new { message = ex.Message, type = "server_error" }
            }, JsonOptions);
            await WriteRawAsync(ctx, $"data: {err}\n\ndata: [DONE]\n\n");
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    private async IAsyncEnumerable<WebChatStreamEvent> ExecuteChatStreamAsync(
        Chat2ApiCompat.CompletionRequest req)
    {
        if (string.IsNullOrWhiteSpace(_config.WebUserToken))
            throw new InvalidOperationException("请先在 DeepSeek 网页登录，本地 API 将自动使用网页会话，无需填写 API Key。");

        var prevRefIds = _web.AgentRefFileIds;
        _web.AgentRefFileIds = req.RefFileIds;

        var dbg = AgentDebugLogger.Current;
        var chatSw = Chat2ApiFeatureScope.HasActiveAgentRun ? System.Diagnostics.Stopwatch.StartNew() : null;
        var firstChunk = true;
        var useThinking = req.Thinking;
        if (Chat2ApiFeatureScope.HasActiveAgentRun && useThinking)
        {
            dbg?.Write("CHAT2API", "Agent/TUI 路径：关闭 thinking 以保证 content 流式输出");
            useThinking = false;
        }

        dbg?.LogChat2ApiRequest(
            req.ResolvedModel,
            useThinking,
            req.WebSearch,
            req.Messages.Count,
            stream: true);

        WebChatResult? finalResult = null;
        await foreach (var ev in _web.WebChatStreamAsync(
                           req.Messages,
                           req.ResolvedModel,
                           useThinking,
                           req.WebSearch,
                           CancellationToken.None,
                           _config.WebUserToken,
                           req.WebChatSessionId))
        {
            if (firstChunk && ev is not WebChatStreamDone)
            {
                firstChunk = false;
                dbg?.Write("CHAT2API", "流式首包到达");
            }

            if (ev is WebChatStreamDone done)
                finalResult = done.Result;
            yield return ev;
        }

        if (finalResult is null && Chat2ApiFeatureScope.HasActiveAgentRun)
        {
            finalResult = new WebChatResult
            {
                Content = "未能从网页会话获取回复，请确认已在「普通对话」登录 DeepSeek 后重试。",
                Model = req.ResolvedModel,
                FinishReason = "stop"
            };
            yield return new WebChatStreamDone(finalResult);
        }

        _web.AgentRefFileIds = prevRefIds;
        if (chatSw is not null)
        {
            chatSw.Stop();
            var chars = finalResult?.Content?.Length ?? 0;
            var reasoning = finalResult?.ReasoningContent?.Length ?? 0;
            var note = finalResult is null
                ? "stream ended without done"
                : (reasoning > 0 && chars == 0 ? $"reasoningChars={reasoning}" : null);
            dbg?.LogChat2ApiDone(
                req.ResolvedModel,
                chatSw.ElapsedMilliseconds,
                chars + reasoning,
                note);
        }

        if (finalResult is not null &&
            !string.IsNullOrWhiteSpace(req.SessionId) &&
            !string.IsNullOrWhiteSpace(finalResult.ChatSessionId))
            _sessions.Bind(_config, req.SessionId, finalResult.ChatSessionId);
        else if (!string.IsNullOrWhiteSpace(req.SessionId))
            _sessions.Touch(_config, req.SessionId);
    }

    private async Task<object> HandleChatCompletionAsync(Chat2ApiCompat.CompletionRequest req)
    {
        var result = await ExecuteChatAsync(req);
        return BuildChatResponse(result, req.RequestedModel);
    }

    private async Task<WebChatResult> ExecuteChatAsync(Chat2ApiCompat.CompletionRequest req)
    {
        var prevRefIds = _web.AgentRefFileIds;
        _web.AgentRefFileIds = req.RefFileIds;

        if (string.IsNullOrWhiteSpace(_config.WebUserToken))
            throw new InvalidOperationException("请先在 DeepSeek 网页登录，本地 API 将自动使用网页会话，无需填写 API Key。");

        var dbg = AgentDebugLogger.Current;
        var chatSw = Chat2ApiFeatureScope.HasActiveAgentRun ? System.Diagnostics.Stopwatch.StartNew() : null;
        var useThinking = req.Thinking;
        if (Chat2ApiFeatureScope.HasActiveAgentRun && useThinking)
            useThinking = false;

        dbg?.LogChat2ApiRequest(
            req.ResolvedModel,
            useThinking,
            req.WebSearch,
            req.Messages.Count,
            stream: false);

        WebChatResult result;
        try
        {
            result = await _web.WebChatAsync(
                req.Messages,
                req.ResolvedModel,
                useThinking,
                req.WebSearch,
                CancellationToken.None,
                _config.WebUserToken,
                req.WebChatSessionId);
        }
        finally
        {
            _web.AgentRefFileIds = prevRefIds;
        }

        if (chatSw is not null)
        {
            chatSw.Stop();
            var chars = result.Content?.Length ?? 0;
            var reasoning = result.ReasoningContent?.Length ?? 0;
            dbg?.LogChat2ApiDone(
                req.ResolvedModel,
                chatSw.ElapsedMilliseconds,
                chars + reasoning,
                reasoning > 0 ? $"reasoningChars={reasoning}" : null);
        }

        if (!string.IsNullOrWhiteSpace(req.SessionId) &&
            !string.IsNullOrWhiteSpace(result.ChatSessionId))
            _sessions.Bind(_config, req.SessionId, result.ChatSessionId);
        else if (!string.IsNullOrWhiteSpace(req.SessionId))
            _sessions.Touch(_config, req.SessionId);

        return result;
    }

    private static object BuildChatResponse(WebChatResult result, string? requestedModel = null)
    {
        object message;
        if (result.ToolCalls is { Count: > 0 })
        {
            message = new
            {
                role = "assistant",
                content = (string?)null,
                reasoning_content = result.ReasoningContent,
                tool_calls = result.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments }
                }).ToArray()
            };
            return new
            {
                id = "chatcmpl-local",
                @object = "chat.completion",
                created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                model = result.Model,
                choices = new[]
                {
                    new { index = 0, message, finish_reason = "tool_calls" }
                },
                usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
            };
        }

        message = new
        {
            role = "assistant",
            content = result.Content ?? "",
            reasoning_content = result.ReasoningContent
        };

        return new
        {
            id = "chatcmpl-local",
            @object = "chat.completion",
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            model = result.Model,
            choices = new[]
            {
                new { index = 0, message, finish_reason = result.FinishReason ?? "stop" }
            },
            usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
        };
    }

    private bool TryAuthorize(HttpListenerContext ctx, out object? error)
    {
        error = null;
        if (LocalApiKeyService.TryValidate(_config, ctx.Request, out var matched))
        {
            if (matched is not null)
                LocalApiKeyService.RecordUsage(_config, matched);
            return true;
        }

        var hasKey = !string.IsNullOrWhiteSpace(LocalApiKeyService.ExtractProvidedKey(ctx.Request));
        error = new
        {
            error = new
            {
                message = hasKey ? "Invalid API key" : "API key is required",
                type = "invalid_request_error",
                code = hasKey ? "invalid_api_key" : "missing_api_key"
            }
        };
        return false;
    }

    private async Task<object> BuildHealthPayloadAsync()
    {
        var health = await _web.ProbeChat2ApiHealthAsync(_config.WebUserToken, BaseUrl);
        var snap = Chat2ApiProviderService.Build(_config, health);
        var keys = _config.LocalApiKeys;
        return new
        {
            status = health.CanChat ? "ok" : "degraded",
            summary = health.Summary,
            api_listening = health.ApiListening,
            config_logged_in = health.ConfigLoggedIn,
            bridge_ready = health.BridgeReady,
            bridge_has_user_token = health.BridgeHasUserToken,
            bridge_page = health.BridgePage,
            base_url = health.BaseUrl,
            api_key_auth_enabled = LocalApiKeyService.ShouldEnforceAuth(_config),
            api_key_count = keys.Count,
            api_key_enabled_count = keys.Count(k => k.Enabled),
            session_mode = _config.Chat2ApiSessionMode,
            active_sessions = _sessions.Count,
            error = health.Error,
            provider = new
            {
                snap.Id,
                snap.Name,
                snap.Type,
                snap.Online,
                snap.AuthType,
                snap.ModelCount,
                snap.Chat2ApiBaseUrl,
                api_key_masked = snap.ApiKeyMasked
            },
            deepseek_tui = new
            {
                runtime_url = snap.TuiRuntimeUrl,
                config_path = snap.TuiConfigPath,
                integration_file = snap.IntegrationFilePath,
                chat2api_base_url = snap.Chat2ApiBaseUrl
            }
        };
    }

    private static async Task WriteJsonAsync(HttpListenerContext ctx, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private static async Task WriteRawAsync(HttpListenerContext ctx, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    public void Dispose() => Stop();
}
