using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.Harness;

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
    private readonly DsdApiSessionStore _sessions = new();
    private IAgentWebChat? _webBridge;
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

    public IReadOnlyList<DsdApiSessionInfo> ListSessions() => _sessions.ListSessions(_config);

    public bool DeleteSession(string sessionId) => _sessions.Delete(sessionId);

    public int CleanExpiredSessions() => _sessions.CleanExpired(_config);

    public void UpdateConfig(AppConfig config)
    {
        var portChanged = InternalChatChannel.ResolveExternalApiPort(_config) !=
                          InternalChatChannel.ResolveExternalApiPort(config);
        var wasEnabled = _config.EnableExternalOpenAiApi;
        _config = config;
        DsdOpenAiCompat.EnsureDefaultMappings(_config);
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
                DsdApiHealth? probe = null;
                try
                {
                    var probeToken = AccountCredentials.ResolveFirstProviderWebToken("deepseek", _config);
                    if (!string.IsNullOrWhiteSpace(probeToken))
                        probe = await _web.ProbeDsdApiHealthAsync(probeToken, BaseUrl);
                }
                catch
                {
                    // ignore: health probe optional for providers listing
                }

                var snap = DsdApiProviderService.Build(_config, probe);
                await WriteJsonAsync(ctx, 200, DsdApiProviderService.ToApiPayload(snap));
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
                    data = DsdOpenAiCompat.ListModels(_config)
                });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                path.StartsWith("/v1/models/", StringComparison.OrdinalIgnoreCase))
            {
                var modelId = Uri.UnescapeDataString(path["/v1/models/".Length..]);
                var model = DsdOpenAiCompat.GetModel(modelId, _config);
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
                    mode = _config.DsdApiSessionMode
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
                var req = DsdOpenAiCompat.ParseCompletion(bodyText, ctx.Request, _config, _sessions);

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
        DsdOpenAiCompat.CompletionRequest req)
    {
        try
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.Add("Cache-Control", "no-cache");
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            var events = ExecuteChatStreamAsync(req);
            await DsdOpenAiSseWriter.PipeWebStreamAsync(
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
        DsdOpenAiCompat.CompletionRequest req)
    {
        var resolution = ApiRouteResolver.Resolve(_config, WebBridge, null, req.RequestedModel);
        var routeModel = resolution.ResolvedModel ?? req.ResolvedModel;
        var webToken = AccountCredentials.ResolveWebUserToken(resolution.Account, _config);
        EnsureChannelReady(resolution);
        var clientSessionId = DsdApiSessionCoordinator.EnsureClientSession(
            req.SessionId,
            resolution.Provider.Id,
            resolution.Account?.Id,
            routeModel);

        var prevRefIds = _web.AgentRefFileIds;
        _web.AgentRefFileIds = req.RefFileIds;

        var dbg = AgentDebugLogger.Current;
        var chatSw = DsdAgentApiScope.HasActiveAgentRun ? System.Diagnostics.Stopwatch.StartNew() : null;
        var firstChunk = true;
        var useThinking = req.Thinking;
        if (DsdAgentApiScope.HasActiveAgentRun && useThinking)
        {
            dbg?.Write("CHAT2API", "Agent/TUI 路径：关闭 thinking 以保证 content 流式输出");
            useThinking = false;
        }

        dbg?.LogDsdApiRequest(
            routeModel,
            useThinking,
            req.WebSearch,
            req.Messages.Count,
            stream: true);

        WebChatResult? finalResult = null;
        var streamHadDone = false;
        var accountId = resolution.Account?.Id;
        try
        {
            await foreach (var ev in resolution.ChatClient.StreamAsync(
                    req.Messages,
                    routeModel,
                    useThinking,
                    req.WebSearch,
                    req.RefFileIds,
                    allowToolCalls: false,
                    CancellationToken.None,
                    webToken,
                    req.WebChatSessionId))
            {
                if (firstChunk && ev is not WebChatStreamDone)
                {
                    firstChunk = false;
                    dbg?.Write("CHAT2API", "流式首包到达");
                }

                if (ev is WebChatStreamDone done)
                {
                    finalResult = done.Result;
                    streamHadDone = true;
                }

                yield return ev;
            }

            if (finalResult is null && DsdAgentApiScope.HasActiveAgentRun)
            {
                finalResult = new WebChatResult
                {
                    Content = "未能获取回复，请确认已在 API 管理中配置有效的 DeepSeek 账户后重试。",
                    Model = routeModel,
                    FinishReason = "stop"
                };
                streamHadDone = true;
                yield return new WebChatStreamDone(finalResult);
            }

            if (accountId is not null && streamHadDone && finalResult is not null)
                ProviderAccountStore.RecordSuccess(accountId);
        }
        finally
        {
            if (accountId is not null && !streamHadDone)
                ProviderAccountStore.RecordFailure(accountId);
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
            dbg?.LogDsdApiDone(
                routeModel,
                chatSw.ElapsedMilliseconds,
                chars + reasoning,
                note);
        }

        if (finalResult is not null &&
            !string.IsNullOrWhiteSpace(clientSessionId) &&
            !string.IsNullOrWhiteSpace(finalResult.ChatSessionId))
        {
            _sessions.Bind(_config, clientSessionId, finalResult.ChatSessionId);
            DsdApiSessionCoordinator.BindProviderSession(
                clientSessionId,
                finalResult.ChatSessionId,
                resolution.Provider.Id,
                resolution.Account?.Id);
        }
        else if (!string.IsNullOrWhiteSpace(clientSessionId))
        {
            _sessions.Touch(_config, clientSessionId);
            DsdApiSessionCoordinator.Touch(clientSessionId);
        }

        RecordRequestLog(req, resolution, routeModel, finalResult, chatSw?.ElapsedMilliseconds ?? 0, stream: true);
    }

    private async Task<object> HandleChatCompletionAsync(DsdOpenAiCompat.CompletionRequest req)
    {
        var result = await ExecuteChatAsync(req);
        return BuildChatResponse(result, req.RequestedModel);
    }

    private async Task<WebChatResult> ExecuteChatAsync(DsdOpenAiCompat.CompletionRequest req)
    {
        var resolution = ApiRouteResolver.Resolve(_config, WebBridge, null, req.RequestedModel);
        var routeModel = resolution.ResolvedModel ?? req.ResolvedModel;
        var webToken = AccountCredentials.ResolveWebUserToken(resolution.Account, _config);
        EnsureChannelReady(resolution);
        var clientSessionId = DsdApiSessionCoordinator.EnsureClientSession(
            req.SessionId,
            resolution.Provider.Id,
            resolution.Account?.Id,
            routeModel);

        var prevRefIds = _web.AgentRefFileIds;
        _web.AgentRefFileIds = req.RefFileIds;

        var dbg = AgentDebugLogger.Current;
        var chatSw = DsdAgentApiScope.HasActiveAgentRun ? System.Diagnostics.Stopwatch.StartNew() : null;
        var useThinking = req.Thinking;
        if (DsdAgentApiScope.HasActiveAgentRun && useThinking)
            useThinking = false;

        dbg?.LogDsdApiRequest(
            routeModel,
            useThinking,
            req.WebSearch,
            req.Messages.Count,
            stream: false);

        WebChatResult result;
        Exception? chatError = null;
        try
        {
            result = await resolution.ChatClient.CompleteAsync(
                req.Messages,
                routeModel,
                useThinking,
                req.WebSearch,
                req.RefFileIds,
                allowToolCalls: false,
                CancellationToken.None,
                webToken,
                req.WebChatSessionId);
            if (resolution.Account is not null)
                ProviderAccountStore.RecordSuccess(resolution.Account.Id);
        }
        catch (Exception ex)
        {
            chatError = ex;
            if (resolution.Account is not null)
                ProviderAccountStore.RecordFailure(resolution.Account.Id);
            throw;
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
            dbg?.LogDsdApiDone(
                routeModel,
                chatSw.ElapsedMilliseconds,
                chars + reasoning,
                reasoning > 0 ? $"reasoningChars={reasoning}" : null);
        }

        if (!string.IsNullOrWhiteSpace(clientSessionId) &&
            !string.IsNullOrWhiteSpace(result.ChatSessionId))
        {
            _sessions.Bind(_config, clientSessionId, result.ChatSessionId);
            DsdApiSessionCoordinator.BindProviderSession(
                clientSessionId,
                result.ChatSessionId,
                resolution.Provider.Id,
                resolution.Account?.Id);
        }
        else if (!string.IsNullOrWhiteSpace(clientSessionId))
        {
            _sessions.Touch(_config, clientSessionId);
            DsdApiSessionCoordinator.Touch(clientSessionId);
        }

        RecordRequestLog(req, resolution, routeModel, result, chatSw?.ElapsedMilliseconds ?? 0, stream: false, chatError);
        return result;
    }

    private static void RecordRequestLog(
        DsdOpenAiCompat.CompletionRequest req,
        ApiRouteResolution resolution,
        string routeModel,
        WebChatResult? result,
        long latencyMs,
        bool stream,
        Exception? error = null)
    {
        try
        {
            var success = error is null && result is not null;
            var preview = result?.Content;
            if (string.IsNullOrWhiteSpace(preview))
                preview = result?.ReasoningContent;

            DsdApiRequestLogStore.Instance.Add(new DsdApiRequestLogStore.RequestLogDraft
            {
                Success = success,
                StatusCode = success ? 200 : 500,
                ResponseStatus = success ? 200 : 500,
                Method = "POST",
                Url = "/v1/chat/completions",
                Model = req.RequestedModel,
                ActualModel = result?.Model ?? routeModel,
                ProviderId = resolution.Provider.Id,
                ProviderName = resolution.Provider.DisplayName,
                AccountId = resolution.Account?.Id ?? "embedded",
                AccountName = resolution.Account?.Name
                    ?? resolution.Provider.DisplayName
                    ?? "DeepSeek Desktop",
                UserInput = ExtractLastUserMessage(req.Messages),
                WebSearch = req.WebSearch,
                ResponsePreview = preview,
                LatencyMs = latencyMs,
                IsStream = stream,
                ErrorMessage = error?.Message
            });
        }
        catch
        {
            // diagnostics only
        }
    }

    private static string? ExtractLastUserMessage(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                return messages[i].Content;
        }

        return null;
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
        DsdApiHealth? health = null;
        try
        {
            var probeToken = AccountCredentials.ResolveFirstProviderWebToken("deepseek", _config);
            if (!string.IsNullOrWhiteSpace(probeToken))
                health = await _web.ProbeDsdApiHealthAsync(probeToken, BaseUrl);
        }
        catch
        {
            health = null;
        }

        health ??= new DsdApiHealth { BaseUrl = BaseUrl, Error = "未配置 DeepSeek API 账户" };
        var snap = DsdApiProviderService.Build(_config, health);
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
            session_mode = _config.DsdApiSessionMode,
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
                snap.DsdApiBaseUrl,
                api_key_masked = snap.ApiKeyMasked
            },
            deepseek_tui = new
            {
                runtime_url = snap.TuiRuntimeUrl,
                config_path = snap.TuiConfigPath,
                integration_file = snap.IntegrationFilePath,
                dsd_api_base_url = snap.DsdApiBaseUrl
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

    private IAgentWebChat WebBridge => _webBridge ??= new WebInjectWebChatAdapter(_web);

    private void EnsureChannelReady(ApiRouteResolution resolution)
    {
        if (resolution.RouteMode == ApiRouteModes.EmbeddedWeb)
        {
            var token = AccountCredentials.ResolveWebUserToken(resolution.Account, _config);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "请先在 API 管理中为 DeepSeek 添加账户并填写用户 Token（可从浏览器 LocalStorage 的 userToken 获取）。");
            return;
        }

        var key = ApiProviderRegistry.ResolveApiKey(resolution.Provider) ?? _config.DeepSeekApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"供应商「{resolution.Provider.DisplayName}」未配置 API Key，请在 API 管理中设置。");
    }

    public void Dispose() => Stop();
}

file sealed class WebInjectWebChatAdapter : IAgentWebChat
{
    private readonly WebInjectService _web;

    public WebInjectWebChatAdapter(WebInjectService web) => _web = web;

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
        var prev = _web.AgentRefFileIds;
        _web.AgentRefFileIds = refFileIds;
        try
        {
            return await _web.WebChatAsync(
                messages, model, thinking, search, ct, webUserToken, webChatSessionId, allowToolCalls);
        }
        finally
        {
            _web.AgentRefFileIds = prev;
        }
    }

    public IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
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
        var prev = _web.AgentRefFileIds;
        _web.AgentRefFileIds = refFileIds;
        return StreamCore(messages, model, thinking, search, allowToolCalls, ct, webUserToken, webChatSessionId, prev);
    }

    private async IAsyncEnumerable<WebChatStreamEvent> StreamCore(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        bool allowToolCalls,
        [EnumeratorCancellation] CancellationToken ct,
        string? webUserToken,
        string? webChatSessionId,
        IReadOnlyList<string> prevRefIds)
    {
        try
        {
            await foreach (var ev in _web.WebChatStreamAsync(
                               messages, model, thinking, search, ct, webUserToken, webChatSessionId, allowToolCalls))
                yield return ev;
        }
        finally
        {
            _web.AgentRefFileIds = prevRefIds;
        }
    }
}
