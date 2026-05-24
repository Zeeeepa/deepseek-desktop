using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekBrowser.Services;

public sealed class WebInjectService : IWebInjectBridge
{
    private readonly WebView2 _webView;
    private readonly WebViewPageKind _pageKind;
    private WebChatBridgeHost? _apiBridge;
    private string? _bridgeScript;
    private string? _workModeClientScript;
    private string? _overlayScript;
    private string? _overlayCss;

    public event EventHandler<JsonElement>? MessageReceived;

    /// <summary>当前 Agent 任务附带的网页端文件 ID（上传后填入 ref_file_ids）。</summary>
    public IReadOnlyList<string> AgentRefFileIds { get; set; } = Array.Empty<string>();

    public WebInjectService(WebView2 webView, WebViewPageKind pageKind = WebViewPageKind.Chat)
    {
        _webView = webView;
        _pageKind = pageKind;
    }

    public void AttachApiBridge(WebChatBridgeHost apiBridge) => _apiBridge = apiBridge;

    public Task SyncApiBridgeTokenAsync(string? webUserToken, CancellationToken ct = default) =>
        _apiBridge?.SyncWebUserTokenAsync(webUserToken) ?? Task.CompletedTask;

    public Task EnsureApiBridgeReadyAsync(CancellationToken ct = default) =>
        _apiBridge?.EnsureReadyAsync(ct) ?? Task.CompletedTask;

    public Task<Chat2ApiHealth?> ProbeChat2ApiHealthAsync(string? configWebUserToken, string baseUrl,
        CancellationToken ct = default) =>
        _apiBridge is null
            ? Task.FromResult(new Chat2ApiHealth
            {
                ApiListening = true,
                ConfigLoggedIn = !string.IsNullOrWhiteSpace(configWebUserToken),
                BaseUrl = baseUrl,
                Error = "桥接 WebView 未附加"
            })
            : RunOnUiAsync(async () =>
            {
                var h = await _apiBridge.ProbeAsync(configWebUserToken, ct);
                return h with { BaseUrl = baseUrl };
            });

    private Dispatcher UiDispatcher => _webView.Dispatcher;

    private Task RunOnUiAsync(Func<Task> action) =>
        UiDispatcher.CheckAccess()
            ? action()
            : UiDispatcher.InvokeAsync(action).Task.Unwrap();

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action) =>
        UiDispatcher.CheckAccess()
            ? action()
            : UiDispatcher.InvokeAsync(action).Task.Unwrap();

    public async Task AttachAsync(CoreWebView2 core)
    {
        LoadAssets();

        var injectDir = Path.Combine(AppContext.BaseDirectory, "Assets", "inject");
        var agentDir = Path.Combine(AppContext.BaseDirectory, "Assets", "agent");
        core.SetVirtualHostNameToFolderMapping(
            "ds-inject.local",
            injectDir,
            CoreWebView2HostResourceAccessKind.Allow);
        if (Directory.Exists(agentDir))
        {
            core.SetVirtualHostNameToFolderMapping(
                "ds-agent.local",
                agentDir,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        var chat2apiDir = Path.Combine(AppContext.BaseDirectory, "Assets", "chat2api");
        if (Directory.Exists(chat2apiDir))
        {
            core.SetVirtualHostNameToFolderMapping(
                "ds-chat2api.local",
                chat2apiDir,
                CoreWebView2HostResourceAccessKind.Allow);
        }

        await core.AddScriptToExecuteOnDocumentCreatedAsync(_bridgeScript!);
        if (!string.IsNullOrEmpty(_workModeClientScript))
            await core.AddScriptToExecuteOnDocumentCreatedAsync(_workModeClientScript);
        if (!string.IsNullOrEmpty(_overlayCss))
        {
            var cssEsc = JsonSerializer.Serialize(_overlayCss);
            if (_pageKind == WebViewPageKind.Chat)
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    "(function(){try{"
                    + "if(/^ds-agent\\.local$/i.test(location.hostname))return;"
                    + "var p=document.head||document.documentElement;"
                    + "if(!p)return;"
                    + $"var s=document.createElement('style');s.textContent={cssEsc};"
                    + "p.appendChild(s);"
                    + "}catch(e){}})();");
            }
        }

        if (_pageKind == WebViewPageKind.Chat)
        {
            var overlayGuarded =
                "if(!/^ds-agent\\.local$/i.test(location.hostname)){" + _overlayScript + "}";
            await core.AddScriptToExecuteOnDocumentCreatedAsync(overlayGuarded);
        }
        core.WebMessageReceived += OnWebMessageReceived;
    }

    public bool IsAgentHostPage
    {
        get
        {
            if (UiDispatcher.CheckAccess())
                return IsAgentHostPageOnUi();
            return UiDispatcher.Invoke(IsAgentHostPageOnUi);
        }
    }

    private bool IsAgentHostPageOnUi() =>
        _pageKind == WebViewPageKind.Agent ||
        AppNavigation.IsAgentPage(_webView.CoreWebView2?.Source);

    public Task TriggerInjectAsync(bool forceReset = false) =>
        RunOnUiAsync(() => TriggerInjectOnUiAsync(forceReset));

    private async Task TriggerInjectOnUiAsync(bool forceReset)
    {
        if (_webView.CoreWebView2 is null || _pageKind == WebViewPageKind.Agent || IsAgentHostPageOnUi()) return;
        var flag = forceReset ? "true" : "false";
        await _webView.CoreWebView2.ExecuteScriptAsync(
            "(function(){"
            + "if(window.__dsNativeTryInject)window.__dsNativeTryInject();"
            + "if(window.__dsNativeOnRouteChange)window.__dsNativeOnRouteChange(" + flag + ");"
            + "})();");
    }

    public async Task BurstInjectAsync(CancellationToken ct = default, bool forceReset = false)
    {
        await TriggerInjectAsync(forceReset);
        foreach (var delay in new[] { 400, 1000, 2200, 4000 })
        {
            await Task.Delay(delay, ct);
            await TriggerInjectAsync(false);
        }
    }

    public async Task ReinjectAsync() => await BurstInjectAsync();

    private static string ResolveInjectDir()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "inject"),
            Path.Combine(AppContext.BaseDirectory, "inject")
        };
        foreach (var dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "bridge.js")) &&
                File.Exists(Path.Combine(dir, "overlay.js")))
                return dir;
        }

        throw new DirectoryNotFoundException(
            "找不到注入资源目录 Assets\\inject（bridge.js / overlay.js）。"
            + "请从源码目录运行 build.ps1 重新部署，或不要只复制 DeepSeek.exe。");
    }

    private static readonly Lazy<(string Bridge, string WorkModeClient, string Overlay, string Css)> InjectAssets = new(() =>
    {
        var baseDir = ResolveInjectDir();
        var themePath = Path.Combine(baseDir, "ds-theme.css");
        var overlayCssPath = Path.Combine(baseDir, "overlay.css");
        if (!File.Exists(overlayCssPath))
            throw new FileNotFoundException(
                $"缺少 {overlayCssPath}。请重新运行 .\\build.ps1，并从 .\\publish\\DeepSeek.exe 启动。", overlayCssPath);

        var overlayCss = File.ReadAllText(overlayCssPath);
        if (File.Exists(themePath))
            overlayCss = File.ReadAllText(themePath) + "\n" + overlayCss;
        return (
            File.ReadAllText(Path.Combine(baseDir, "bridge.js")),
            File.ReadAllText(Path.Combine(baseDir, "work-mode-client.js")),
            File.ReadAllText(Path.Combine(baseDir, "overlay.js")),
            overlayCss);
    });

    private void LoadAssets()
    {
        var assets = InjectAssets.Value;
        _bridgeScript = GuardDocumentCreatedScript(assets.Bridge, "bridge");
        _workModeClientScript = GuardDocumentCreatedScript(assets.WorkModeClient, "work-mode");
        _overlayScript = assets.Overlay;
        _overlayCss = assets.Css;
    }

    /// <summary>Chat2API 内嵌 iframe 不注入官网桥脚本，避免破坏 React 首屏。</summary>
    private static string GuardDocumentCreatedScript(string script, string name) =>
        "(function(){try{"
        + "var h=(location.hostname||'').toLowerCase(),p=location.pathname||'';"
        + "if(h==='ds-chat2api.local')return;"
        + "if(h==='ds-agent.local'&&p.indexOf('/chat2api/')!==-1)return;"
        + script
        + "}catch(e){console.warn('[DeepSeek Desktop] inject " + name + "',e);}})();";

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json)) return;
            using var doc = JsonDocument.Parse(json);
            MessageReceived?.Invoke(this, doc.RootElement.Clone());
        }
        catch
        {
            // ignore malformed messages
        }
    }

    public Task PostToPageAsync(object message) =>
        RunOnUiAsync(() => PostToPageOnUiAsync(message));

    public Task<string?> EvaluateOnPageAsync(string javaScriptExpression) =>
        RunOnUiAsync(async () =>
        {
            if (_webView.CoreWebView2 is null) return null;
            return await _webView.CoreWebView2.ExecuteScriptAsync(javaScriptExpression);
        });

    /// <summary>向 WebView 所有 frame 广播 postMessage（内嵌 iframe 的 Chat2API / 设置页使用）。</summary>
    public Task PostWebMessageAsync(object message) =>
        RunOnUiAsync(() => PostWebMessageOnUiAsync(message));

    private Task PostWebMessageOnUiAsync(object message)
    {
        if (_webView.CoreWebView2 is null) return Task.CompletedTask;
        if (message is Task)
            throw new InvalidOperationException("Cannot post an unawaited Task to the WebView; await IPC handlers before replying.");
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, AgentSessionJson.Options));
        return Task.CompletedTask;
    }

    public Task PushWorkModeStateAsync(WorkModeStatePayload state) =>
        RunOnUiAsync(() => PushWorkModeStateOnUiAsync(state));

    private async Task PushWorkModeStateOnUiAsync(WorkModeStatePayload state)
    {
        if (_webView.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(state, AgentSessionJson.Options);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            "(function(m){try{"
            + "if(window.DsWorkMode&&window.DsWorkMode.applyState){window.DsWorkMode.applyState(m);return;}"
            + "if(typeof window.__dsApplyWorkModeState==='function')window.__dsApplyWorkModeState(m);"
            + "else{(window.__dsPendingNativeMessages=window.__dsPendingNativeMessages||[]).push(m);}"
            + "}catch(e){console.warn('[DeepSeek Desktop] workModeState',e);}"
            + "})(" + json + ");");
    }

    private async Task PostToPageOnUiAsync(object message)
    {
        if (_webView.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(message, AgentSessionJson.Options);
        await _webView.CoreWebView2.ExecuteScriptAsync(
            "(function(m){"
            + "try{"
            + "if(typeof window.dsDesktopOnMessage==='function'){window.dsDesktopOnMessage(m);return;}"
            + "window.__dsPendingNativeMessages=window.__dsPendingNativeMessages||[];"
            + "window.__dsPendingNativeMessages.push(m);"
            + "}catch(e){console.warn('[DeepSeek Desktop] native msg',e);}"
            + "})(" + json + ");");
    }

    public Task PushAgentAuthHintAsync(bool loggedIn) =>
        RunOnUiAsync(() => PushAgentAuthHintOnUiAsync(loggedIn));

    private async Task PushAgentAuthHintOnUiAsync(bool loggedIn)
    {
        if (_webView.CoreWebView2 is null || !IsAgentHostPageOnUi()) return;
        var lit = loggedIn ? "true" : "false";
        await _webView.CoreWebView2.ExecuteScriptAsync(
            "window.__dsLoggedIn=" + lit + ";"
            + "if(typeof window.dsAgentApplyAuth==='function')window.dsAgentApplyAuth(" + lit + ");");
    }

    private static JsonElement ParseBridgeEnvelope(string raw)
    {
        using var outer = JsonDocument.Parse(raw);
        var root = outer.RootElement;

        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (string.IsNullOrWhiteSpace(inner))
                throw new InvalidOperationException("网页桥接无响应");
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

    private static bool IsBridgeOk(JsonElement envelope)
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

    private static string ExtractBridgeData(string raw)
    {
        var envelope = ParseBridgeEnvelope(raw);
        return ExtractBridgePayload(envelope, raw, 0);
    }

    private static string ExtractBridgePayload(JsonElement envelope, string rawHint, int depth)
    {
        if (depth > 4)
            throw new InvalidOperationException("网页桥接响应嵌套过深");

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
                    throw new InvalidOperationException("网页桥接 data 为空");

                if (inner.TrimStart().StartsWith('{') || inner.TrimStart().StartsWith('['))
                {
                    using var innerDoc = JsonDocument.Parse(inner);
                    return ExtractBridgePayload(innerDoc.RootElement, rawHint, depth + 1);
                }

                return JsonSerializer.Serialize(inner);
            }

            if (dataEl.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return dataEl.GetRawText();
        }

        if (IsBridgeOk(envelope) && envelope.TryGetProperty("data", out var okData))
            return ExtractBridgePayload(okData, rawHint, depth + 1);

        if (!IsBridgeOk(envelope))
        {
            var err = envelope.TryGetProperty("error", out var errEl) ? JsonElementToText(errEl) : null;
            if (string.IsNullOrWhiteSpace(err) && envelope.TryGetProperty("msg", out var msgEl))
                err = JsonElementToText(msgEl);
            if (string.IsNullOrWhiteSpace(err))
            {
                var snippet = rawHint.Length > 280 ? rawHint[..280] + "…" : rawHint;
                err = "网页桥接失败: " + snippet;
            }

            throw new InvalidOperationException(err ?? "网页桥接失败");
        }

        throw new InvalidOperationException("网页桥接无 data 字段");
    }

    private static string? JsonElementToText(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => el.GetRawText()
        };

    public Task<string?> ExecuteBridgeAsync(string expression, CancellationToken ct = default) =>
        RunOnUiAsync(() => ExecuteBridgeOnUiAsync(expression, ct));

    private async Task<string?> ExecuteBridgeOnUiAsync(string expression, CancellationToken ct)
    {
        if (_apiBridge is not null && (IsAgentHostPageOnUi() || _webView.CoreWebView2 is null))
            return await _apiBridge.ExecuteBridgeAsync(expression, ct);

        ct.ThrowIfCancellationRequested();
        if (_webView.CoreWebView2 is null) return null;
        var wrapped =
            "(async function(){ try { return JSON.stringify({ ok: true, data: await (" + expression + ") }); } catch(e) { return JSON.stringify({ ok: false, error: String(e.message || e) }); } })()";
        var raw = await _webView.CoreWebView2.ExecuteScriptAsync(wrapped);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        return ExtractBridgeData(raw);
    }

    public Task<string?> GetUserTokenAsync(bool waitForBridge = true) =>
        waitForBridge
            ? (_apiBridge is not null
                ? _apiBridge.ExecuteBridgeAsync("window.dsDesktopBridge && window.dsDesktopBridge.getToken()")
                : ExecuteBridgeAsync("window.dsDesktopBridge && window.dsDesktopBridge.getToken()"))
            : TryReadUserTokenQuickAsync();

    /// <summary>从桥接页或主 WebView 读取 userToken（不等待桥接 90s 超时）。</summary>
    public async Task<string?> TryReadUserTokenAsync() => await TryReadUserTokenQuickAsync();

    private async Task<string?> TryReadUserTokenQuickAsync()
    {
        try
        {
            if (_apiBridge is not null)
            {
                var fromBridge = await _apiBridge.TryExecuteBridgeIfReadyAsync(
                    "window.dsDesktopBridge && window.dsDesktopBridge.getToken()");
                var t = NormalizeTokenRaw(fromBridge);
                if (!string.IsNullOrWhiteSpace(t))
                    return t;
            }

            if (!IsAgentHostPageOnUi() && _webView.CoreWebView2 is not null)
            {
                var raw = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "(function(){try{"
                    + "if(window.dsDesktopBridge&&window.dsDesktopBridge.getToken)"
                    + "return window.dsDesktopBridge.getToken();"
                    + "return localStorage.getItem('userToken')||'';"
                    + "}catch(e){return '';}})()");
                return NormalizeTokenRaw(raw);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeTokenRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        return raw.Trim().Trim('"');
    }

    private async Task InjectWebUserTokenOnUiAsync(string? webUserToken)
    {
        if (_webView.CoreWebView2 is null || string.IsNullOrWhiteSpace(webUserToken))
            return;
        var esc = JsonSerializer.Serialize(webUserToken);
        await _webView.CoreWebView2.ExecuteScriptAsync($"window.__dsWebUserToken={esc};");
    }

    public IAsyncEnumerable<WebChatStreamEvent> WebChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        bool allowToolCalls = false)
    {
        if (_apiBridge is null)
            throw new InvalidOperationException("流式 Chat 需要 Chat2API 桥接 WebView，请重启应用。");

        return _apiBridge.WebChatStreamAsync(
            messages, model, thinking, search, AgentRefFileIds, ct, webUserToken, webChatSessionId, allowToolCalls);
    }

    public Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null,
        bool allowToolCalls = false) =>
        RunOnUiAsync(() => WebChatOnUiAsync(messages, model, thinking, search, ct, webUserToken, webChatSessionId, allowToolCalls));

    private async Task<WebChatResult> WebChatOnUiAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken,
        string? webChatSessionId,
        bool allowToolCalls)
    {
        if (_apiBridge is not null)
        {
            return await _apiBridge.WebChatAsync(
                messages, model, thinking, search, AgentRefFileIds, ct, webUserToken, webChatSessionId, allowToolCalls);
        }

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

        var msgJson = JsonSerializer.Serialize(payload);
        var optsJson = JsonSerializer.Serialize(new
        {
            thinking,
            search,
            modelType = "expert",
            refFileIds = AgentRefFileIds,
            chatSessionId = webChatSessionId,
            suppressToolCalls = Chat2ApiFeatureScope.HasActiveAgentRun
        });
        await InjectWebUserTokenOnUiAsync(webUserToken);

        var expr = $"window.dsDesktopBridge.webChatCompletion({msgJson}, {JsonSerializer.Serialize(model)}, {optsJson})";
        var raw = await ExecuteBridgeOnUiAsync(expr, ct)
                  ?? throw new InvalidOperationException("网页桥接无响应");

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

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
                    Arguments = JsonElementToText(tc.GetProperty("function").GetProperty("arguments")) ?? "{}"
                });
            }
        }

        var content = root.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null
            ? JsonElementToText(c)
            : null;
        var finishReason = root.TryGetProperty("finish_reason", out var fr)
            ? JsonElementToText(fr)
            : null;
        var likelyFromBridge = root.TryGetProperty("is_likely_truncated", out var lt) && lt.ValueKind == JsonValueKind.True;
        var likely = likelyFromBridge || AdaptiveOutputTokenEscalation.DetectHeuristicTruncation(content);

        var chatSessionId = root.TryGetProperty("chat_session_id", out var cs)
            ? JsonElementToText(cs)
            : null;

        return new WebChatResult
        {
            Content = content,
            ReasoningContent = root.TryGetProperty("reasoning_content", out var r)
                ? JsonElementToText(r)
                : null,
            ToolCalls = toolCalls,
            Model = root.TryGetProperty("model", out var modelEl)
                ? JsonElementToText(modelEl) ?? model
                : model,
            FinishReason = finishReason ?? (likely ? "length" : "stop"),
            IsLikelyTruncated = likely,
            ChatSessionId = chatSessionId
        };
    }
}
