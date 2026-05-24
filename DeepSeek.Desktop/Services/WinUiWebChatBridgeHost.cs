using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using DeepSeekBrowser.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DeepSeek.Desktop.Services;

/// <summary>
/// ?? WebView??? <c>chat.deepseek.com</c>?? Agent ????? Chat API??? ds-agent.local ????
/// </summary>
public sealed class WinUiWebChatBridgeHost
{
    private readonly WebView2 _webView;
    private readonly ConcurrentDictionary<string, WebChatStreamHub> _streamHubs = new();
    private TaskCompletionSource<bool> _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _navigated;
    private string? _pendingWebUserToken;
    private string? _bridgeScript;

    public WinUiWebChatBridgeHost(WebView2 webView) => _webView = webView;

    private Microsoft.UI.Dispatching.DispatcherQueue UiQueue => _webView.DispatcherQueue;

    public async Task AttachAndNavigateAsync(CoreWebView2Environment env)
    {
        await UiQueue.InvokeAsync(async () =>
        {
            await _webView.EnsureCoreWebView2Async(env);
            var core = _webView.CoreWebView2
                       ?? throw new InvalidOperationException("????? Chat2API ?? WebView");

            core.Settings.IsWebMessageEnabled = true;
            core.Settings.IsScriptEnabled = true;

            var injectDir = Path.Combine(AppContext.BaseDirectory, "Assets", "inject");
            core.SetVirtualHostNameToFolderMapping(
                "ds-inject.local",
                injectDir,
                CoreWebView2HostResourceAccessKind.Allow);

            var bridgePath = Path.Combine(injectDir, "bridge.js");
            _bridgeScript = File.ReadAllText(bridgePath);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(_bridgeScript);
            core.WebMessageReceived += OnBridgeWebMessageReceived;
            core.NavigationCompleted += OnNavigationCompleted;
            core.Navigate(AppNavigation.DeepSeekUrl);
            _navigated = true;
        });
    }

    private void OnBridgeWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json)) return;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("streamId", out var sidEl)) return;
            var sid = sidEl.GetString();
            if (string.IsNullOrEmpty(sid)) return;
            if (_streamHubs.TryGetValue(sid, out var hub))
                hub.TryHandleMessage(root);
        }
        catch
        {
            // ignore malformed stream messages
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return;
        }

        var src = _webView.CoreWebView2?.Source ?? "";
        if (!src.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase)) return;
        _ = VerifyBridgeAsync();
    }

    public Task SyncWebUserTokenAsync(string? webUserToken) =>
        RunOnUiAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(webUserToken))
                return;
            _pendingWebUserToken = webUserToken;
            if (_webView.CoreWebView2 is null) return;
            var esc = JsonSerializer.Serialize(webUserToken);
            await _webView.CoreWebView2.ExecuteScriptAsync($"window.__dsWebUserToken={esc};");
        });

    private async Task EnsureBridgeScriptInjectedAsync()
    {
        var core = _webView.CoreWebView2;
        if (core is null || string.IsNullOrEmpty(_bridgeScript))
            return;

        var typeRaw = await core.ExecuteScriptAsync("typeof window.dsDesktopBridge");
        if (typeRaw?.Contains("object", StringComparison.OrdinalIgnoreCase) == true)
            return;

        var reinject = _bridgeScript.Replace(
            "if (window.__dsBridge) return;",
            "try{delete window.__dsBridge;delete window.dsDesktopBridge;}catch(e){}",
            StringComparison.Ordinal);
        await core.ExecuteScriptAsync(reinject);
    }

    private async Task<bool> TryMarkBridgeReadyAsync(CancellationToken ct)
    {
        await EnsureBridgeScriptInjectedAsync();
        await InjectPendingTokenOnUiAsync();

        try
        {
            _ = await ExecuteBridgeScriptAsync(
                "window.dsDesktopBridge?window.dsDesktopBridge.ping():null", ct);
            _readyTcs.TrySetResult(true);
            return true;
        }
        catch
        {
            var typeRaw = await _webView.CoreWebView2!.ExecuteScriptAsync("typeof window.dsDesktopBridge");
            if (typeRaw?.Contains("object", StringComparison.OrdinalIgnoreCase) == true)
            {
                _readyTcs.TrySetResult(true);
                return true;
            }
        }

        return false;
    }

    private async Task VerifyBridgeAsync()
    {
        try
        {
            for (var i = 0; i < 60; i++)
            {
                if (await TryMarkBridgeReadyAsync(CancellationToken.None))
                    return;
                await Task.Delay(300);
            }
        }
        catch
        {
            // retry on next navigation
        }
    }

    private async Task InjectPendingTokenOnUiAsync()
    {
        if (string.IsNullOrWhiteSpace(_pendingWebUserToken) || _webView.CoreWebView2 is null)
            return;
        var esc = JsonSerializer.Serialize(_pendingWebUserToken);
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.__dsWebUserToken={esc};");
    }

    public Task EnsureReadyAsync(CancellationToken ct = default) =>
        RunOnUiAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (_readyTcs.Task.IsCompletedSuccessfully)
                return;

            var core = _webView.CoreWebView2
                       ?? throw new InvalidOperationException("Chat2API ?? WebView ????");

            if (!_navigated)
                core.Navigate(AppNavigation.DeepSeekUrl);

            await InjectPendingTokenOnUiAsync();

            var deadline = DateTime.UtcNow.AddSeconds(90);
            var navRetries = 0;
            var lastNav = DateTime.UtcNow;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                if (_readyTcs.Task.IsCompletedSuccessfully)
                    return;

                try
                {
                    if (await TryMarkBridgeReadyAsync(ct))
                        return;
                }
                catch
                {
                    // ???? / ????
                }

                if (navRetries < 4 && (DateTime.UtcNow - lastNav).TotalSeconds >= 20)
                {
                    navRetries++;
                    lastNav = DateTime.UtcNow;
                    core.Navigate(AppNavigation.DeepSeekUrl);
                }

                await Task.Delay(400, ct);
            }

            throw new TimeoutException(
                "Chat2API ?????????? 90 ???? chat.deepseek.com ??? bridge.js??????/????????????? DeepSeek ????");
        });

    public Task<Chat2ApiHealth> ProbeAsync(string? configWebUserToken, CancellationToken ct = default) =>
        RunOnUiAsync(async () =>
        {
            var health = new Chat2ApiHealth
            {
                ApiListening = true,
                ConfigLoggedIn = !string.IsNullOrWhiteSpace(configWebUserToken),
                BridgePage = _webView.CoreWebView2?.Source
            };

            if (!string.IsNullOrWhiteSpace(configWebUserToken))
                await SyncWebUserTokenAsync(configWebUserToken);

            try
            {
                await EnsureReadyAsync(ct);
                health = health with { BridgeReady = true, BridgePage = _webView.CoreWebView2?.Source };

                var tokenRaw = await ExecuteBridgeScriptAsync(
                    "window.dsDesktopBridge && window.dsDesktopBridge.getToken()", ct);
                health = health with
                {
                    BridgeHasUserToken = !string.IsNullOrWhiteSpace(NormalizeBridgeToken(tokenRaw))
                };
            }
            catch (Exception ex)
            {
                health = health with { Error = ex.Message };
            }

            return health;
        });

    private static string? NormalizeBridgeToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t is "null" or "\"\"" or "''") return null;
        return t.Trim('"');
    }

    public bool IsBridgeReady => _readyTcs.Task.IsCompletedSuccessfully;

    /// <summary>????????????????? null????? UI ????</summary>
    public Task<string?> TryExecuteBridgeIfReadyAsync(string expression, CancellationToken ct = default) =>
        RunOnUiAsync(async () =>
        {
            if (!IsBridgeReady || _webView.CoreWebView2 is null)
                return null;
            try
            {
                return await ExecuteBridgeScriptAsync(expression, ct);
            }
            catch
            {
                return null;
            }
        });

    public Task<string?> ExecuteBridgeAsync(string expression, CancellationToken ct = default) =>
        RunOnUiAsync(async () =>
        {
            await EnsureReadyAsync(ct);
            return await ExecuteBridgeScriptAsync(expression, ct);
        });

    public Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        RunOnUiAsync(async () =>
        {
            await EnsureReadyAsync(ct);
            if (!string.IsNullOrWhiteSpace(webUserToken))
                await SyncWebUserTokenAsync(webUserToken);
            await InjectPendingTokenOnUiAsync();

            var payload = BuildMessagePayload(messages);
            var msgJson = JsonSerializer.Serialize(payload);
            var optsJson = JsonSerializer.Serialize(new
            {
                thinking,
                search,
                modelType = "expert",
                refFileIds = refFileIds.ToArray(),
                chatSessionId = webChatSessionId,
                suppressToolCalls = Chat2ApiFeatureScope.HasActiveAgentRun
            });
            var expr =
                $"window.dsDesktopBridge.webChatCompletion({msgJson}, {JsonSerializer.Serialize(model)}, {optsJson})";
            var raw = await ExecuteBridgeScriptAsync(expr, ct)
                      ?? throw new InvalidOperationException("???????");
            return ParseWebChatResult(raw, model);
        });

    public async IAsyncEnumerable<WebChatStreamEvent> WebChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null)
    {
        await EnsureReadyAsync(ct);
        await RunOnUiAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(webUserToken))
                await SyncWebUserTokenAsync(webUserToken);
            await InjectPendingTokenOnUiAsync();
        });

        var hub = new WebChatStreamHub(model);
        if (!_streamHubs.TryAdd(hub.StreamId, hub))
            throw new InvalidOperationException("????????");

        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = MonitorStreamStallAsync(hub, stallCts.Token);

        var payload = BuildMessagePayload(messages);
        var msgJson = JsonSerializer.Serialize(payload);
        var optsJson = JsonSerializer.Serialize(new
        {
            thinking,
            search,
            modelType = "expert",
            refFileIds = refFileIds.ToArray(),
            chatSessionId = webChatSessionId,
            suppressToolCalls = Chat2ApiFeatureScope.HasActiveAgentRun
        });
        var streamIdJson = JsonSerializer.Serialize(hub.StreamId);
        var modelJson = JsonSerializer.Serialize(model);
        var startScript =
            "(function(){try{"
            + "var sid=" + streamIdJson + ";"
            + "var run=window.dsDesktopBridge&&window.dsDesktopBridge.webChatCompletionStreaming;"
            + "if(!run)throw new Error('dsDesktopBridge ???');"
            + "run.call(window.dsDesktopBridge,sid," + msgJson + "," + modelJson + "," + optsJson + ")"
            + ".catch(function(e){"
            + "try{window.chrome.webview.postMessage(JSON.stringify({channel:'bridge_stream',streamId:sid,type:'error',message:String((e&&(e.message||e.stack))||e||'stream error')}));}"
            + "catch(x){}"
            + "});"
            + "return JSON.stringify({ok:true,data:true});"
            + "}catch(e){return JSON.stringify({ok:false,error:String((e&&(e.message||e.stack))||e||'start failed')});}})()";

        await RunOnUiAsync(async () =>
        {
            try
            {
                await EnsureBridgeScriptInjectedAsync();
                await InjectPendingTokenOnUiAsync();
                var raw = await ExecuteBridgeScriptRawAsync(startScript);
                if (string.IsNullOrWhiteSpace(raw) || raw == "null")
                    throw new InvalidOperationException("????????????");
                if (raw.Contains("\"ok\":false", StringComparison.OrdinalIgnoreCase))
                {
                    var err = WebBridgeResponse.ParseData(raw);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "????????" : err);
                }
            }
            catch (Exception ex)
            {
                hub.PushError(ex.Message);
            }
        });

        try
        {
            await foreach (var ev in hub.ReadAllAsync(ct))
                yield return ev;
        }
        finally
        {
            stallCts.Cancel();
            _streamHubs.TryRemove(hub.StreamId, out _);
            hub.Dispose();
        }
    }

    private static async Task MonitorStreamStallAsync(WebChatStreamHub hub, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(90), ct);
            if (!hub.HasReceivedDelta)
            {
                hub.PushError(
                    "?? Chat ? 90 ?????????????????? DeepSeek??????????????????????");
            }
        }
        catch (OperationCanceledException)
        {
            // stream progressed or finished
        }
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action) =>
        UiQueue.HasThreadAccess
            ? action()
            : UiQueue.InvokeAsync(action);

    private Task RunOnUiAsync(Func<Task> action) =>
        UiQueue.HasThreadAccess
            ? action()
            : UiQueue.InvokeAsync(action);

    private async Task<string?> ExecuteBridgeScriptAsync(string expression, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var raw = await ExecuteBridgeScriptRawAsync(expression);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        return WebBridgeResponse.ParseData(raw);
    }

    private async Task<string?> ExecuteBridgeScriptRawAsync(string expression)
    {
        if (_webView.CoreWebView2 is null) return null;
        var wrapped =
            "(async function(){ try { "
            + "var r=await(" + expression + "); "
            + "return JSON.stringify({ ok: true, data: r }); "
            + "} catch(e) { "
            + "var m=(e&&(e.message||e.stack))?String(e.message||e.stack):(typeof e==='string'?e:JSON.stringify(e)); "
            + "return JSON.stringify({ ok: false, error: m||'unknown' }); "
            + "} })()";
        return await _webView.CoreWebView2.ExecuteScriptAsync(wrapped);
    }

    private static List<object> BuildMessagePayload(IReadOnlyList<ChatMessage> messages)
    {
        var payload = new List<object>();
        foreach (var m in messages)
        {
            if (m.ToolCalls is { Count: > 0 })
            {
                payload.Add(new
                {
                    role = m.Role,
                    content = (string?)null,
                    tool_calls = m.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new { name = tc.Name, arguments = tc.Arguments }
                    }).ToArray()
                });
            }
            else if (m.Role == "tool")
            {
                payload.Add(new { role = m.Role, content = m.Content, tool_call_id = m.ToolCallId });
            }
            else
            {
                payload.Add(new { role = m.Role, content = m.Content });
            }
        }

        return payload;
    }

    internal static WebChatResult ParseWebChatResultFromJson(JsonElement root, string model) =>
        ParseWebChatResultCore(root, model);

    private static WebChatResult ParseWebChatResult(string raw, string model)
    {
        using var doc = JsonDocument.Parse(raw);
        return ParseWebChatResultCore(doc.RootElement, model);
    }

    private static WebChatResult ParseWebChatResultCore(JsonElement root, string model)
    {

        List<WebToolCall>? toolCalls = null;
        if (root.TryGetProperty("tool_calls", out var tcEl) && tcEl.ValueKind == JsonValueKind.Array)
        {
            toolCalls = new List<WebToolCall>();
            foreach (var tc in tcEl.EnumerateArray())
            {
                toolCalls.Add(new WebToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                    Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                    Arguments = WebBridgeResponse.JsonElementToText(
                                    tc.GetProperty("function").GetProperty("arguments")) ?? "{}"
                });
            }
        }

        var content = root.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
            ? WebBridgeResponse.JsonElementToText(c)
            : null;
        var finishReason = root.TryGetProperty("finish_reason", out var fr)
            ? WebBridgeResponse.JsonElementToText(fr)
            : null;
        var likelyFromBridge = root.TryGetProperty("is_likely_truncated", out var lt)
                               && lt.ValueKind == JsonValueKind.True;
        var likely = likelyFromBridge || AdaptiveOutputTokenEscalation.DetectHeuristicTruncation(content);

        var chatSessionId = root.TryGetProperty("chat_session_id", out var cs)
            ? WebBridgeResponse.JsonElementToText(cs)
            : null;

        return new WebChatResult
        {
            Content = content,
            ReasoningContent = root.TryGetProperty("reasoning_content", out var r)
                ? WebBridgeResponse.JsonElementToText(r)
                : null,
            ToolCalls = toolCalls,
            Model = root.TryGetProperty("model", out var modelEl)
                ? WebBridgeResponse.JsonElementToText(modelEl) ?? model
                : model,
            FinishReason = finishReason ?? (likely ? "length" : "stop"),
            IsLikelyTruncated = likely,
            ChatSessionId = chatSessionId
        };
    }

}

/// <summary>?? bridge.js ExecuteScript ??? JSON ???</summary>
internal static class WebBridgeResponse
{
    public static string ParseData(string raw)
    {
        var envelope = ParseEnvelope(raw);
        return ExtractPayload(envelope, raw, 0);
    }

    private static JsonElement ParseEnvelope(string raw)
    {
        using var outer = JsonDocument.Parse(raw);
        var root = outer.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner))
                throw new InvalidOperationException("???????");
            using var innerDoc = JsonDocument.Parse(inner);
            return innerDoc.RootElement.Clone();
        }

        return root.Clone();
    }

    private static bool LooksLikeChatResult(JsonElement el) =>
        el.ValueKind == JsonValueKind.Object &&
        (el.TryGetProperty("content", out _) ||
         el.TryGetProperty("tool_calls", out _) ||
         el.TryGetProperty("reasoning_content", out _));

    private static bool IsOk(JsonElement envelope)
    {
        if (!envelope.TryGetProperty("ok", out var okEl)) return false;
        return okEl.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => string.Equals(okEl.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string ExtractPayload(JsonElement envelope, string rawHint, int depth)
    {
        if (depth > 4)
            throw new InvalidOperationException("??????????");

        if (LooksLikeChatResult(envelope))
            return envelope.GetRawText();

        if (envelope.TryGetProperty("data", out var dataEl))
        {
            if (LooksLikeChatResult(dataEl))
                return dataEl.GetRawText();

            if (dataEl.ValueKind == JsonValueKind.String)
            {
                var inner = dataEl.GetString();
                if (string.IsNullOrWhiteSpace(inner))
                    throw new InvalidOperationException("???? data ??");
                if (inner.TrimStart().StartsWith('{') || inner.TrimStart().StartsWith('['))
                {
                    using var innerDoc = JsonDocument.Parse(inner);
                    return ExtractPayload(innerDoc.RootElement, rawHint, depth + 1);
                }

                return JsonSerializer.Serialize(inner);
            }

            if (dataEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return dataEl.GetRawText();
        }

        if (IsOk(envelope) && envelope.TryGetProperty("data", out var okData))
            return ExtractPayload(okData, rawHint, depth + 1);

        if (!IsOk(envelope))
        {
            var err = envelope.TryGetProperty("error", out var errEl) ? JsonElementToText(errEl) : null;
            if (string.IsNullOrWhiteSpace(err) || err == "{}")
                err = "dsDesktopBridge ??????????????????? DeepSeek?";
            throw new InvalidOperationException(err);
        }

        throw new InvalidOperationException("????? data ??");
    }

    public static string? JsonElementToText(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => el.GetRawText()
        };
}
