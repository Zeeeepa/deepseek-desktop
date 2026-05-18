using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.DeepSeekTui;
using DeepSeekBrowser.Views;

namespace DeepSeekBrowser.Services;

public sealed class DesktopAgentHost : IAsyncDisposable
{
    private readonly McpHub _mcpHub = new();
    private readonly DeepSeekTuiHost _tuiHost = new();
    private DeepSeekTuiAgentRunner? _tuiAgent;
    private readonly DesktopWebHost _pages;
    private readonly LocalOpenAiServer _localApi;
    private readonly AgentSessionStore _agentSessions = new();
    private AppConfig _config = new();
    private CancellationTokenSource? _runCts;
    private Window? _owner;
    private AgentRunWindow? _agentWindow;

    public Func<string, Task>? NavigateToUrl { get; set; }

    private static Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? action()
            : dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private async Task NavigateForWorkModeAsync(string targetUrl, string mode)
    {
        if (NavigateToUrl is null) return;

        await RunOnUiAsync(async () =>
        {
            await NavigateToUrl(targetUrl);
            if (mode == "chat")
                await _pages.TriggerChatInjectAsync(forceReset: false);
        });
    }

    public DesktopAgentHost(DesktopWebHost pages, LocalOpenAiServer localApi)
    {
        _pages = pages;
        _localApi = localApi;
        _pages.MessageReceived += OnWebMessage;
    }

    public void SetOwner(Window owner)
    {
        _owner = owner;
    }

    private Task<bool> RequestToolApprovalAsync(string toolName, string detail) =>
        RequestToolApprovalAsync(toolName, detail, "执行工具", "DeepSeek-TUI 工具审批");

    private Task<bool> RequestToolApprovalAsync(string toolName, string detail, string action, string title)
    {
        var tcs = new TaskCompletionSource<bool>();
        Application.Current.Dispatcher.Invoke(() =>
        {
            var allowed = Views.DsMessageDialog.Confirm(
                _owner,
                $"Agent 请求{action}：\n\n工具: {toolName}\n\n{detail}\n\n是否允许？",
                title,
                "允许",
                "拒绝");
            tcs.TrySetResult(allowed);
        });
        return tcs.Task;
    }

    private DeepSeekTuiAgentRunner GetTuiAgent() =>
        _tuiAgent ??= new DeepSeekTuiAgentRunner(_tuiHost, (tool, desc) => RequestToolApprovalAsync(tool, desc));

    public void Start()
    {
        _config = ConfigStore.Load();
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        _localApi.UpdateConfig(_config);
        _localApi.Start();
        if (_config.AgentSessionAutoCleanup)
            _agentSessions.ApplyRetentionPolicy(_config);
        _ = ConnectEnabledMcpServersAsync();
        _ = WarmChat2ApiBridgeAsync();
        _ = WarmDeepSeekTuiAsync();
    }

    private async Task WarmDeepSeekTuiAsync()
    {
        try
        {
            ReloadConfig();
            if (string.IsNullOrWhiteSpace(_config.WebUserToken))
                return;
            await DeepSeekTuiBundle.EnsureBinariesAsync(_config).ConfigureAwait(false);
            DeepSeekTuiConfigSync.Apply(_config);
            await _tuiHost.EnsureRunningAsync(_config, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // 启动预热失败不阻塞主界面；发送 Agent 消息时会再次尝试
        }
    }

    public async Task WarmChat2ApiBridgeAsync()
    {
        ReloadConfig();
        if (!string.IsNullOrWhiteSpace(_config.WebUserToken))
            await _pages.SyncApiBridgeTokenAsync(_config.WebUserToken);
        try
        {
            await _pages.EnsureApiBridgeReadyAsync();
        }
        catch
        {
            // 启动时网络未就绪可忽略，发消息前会再次探测
        }
    }

    private async Task ConnectEnabledMcpServersAsync()
    {
        await _mcpHub.ConnectEnabledAsync(_config.McpServers, _ => { }, CancellationToken.None);
    }

    public void ReloadConfig()
    {
        _config = ConfigStore.Load();
        _localApi.UpdateConfig(_config);
    }

    public async Task NotifyApiInfoAsync()
    {
        ReloadConfig();
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);
        Chat2ApiHealth? health = null;
        try
        {
            health = await _pages.ProbeChat2ApiHealthAsync(_config.WebUserToken, _localApi.BaseUrl);
        }
        catch
        {
            // ignore
        }

        var snap = Chat2ApiProviderService.Build(_config, health);
        Chat2ApiProviderService.WriteIntegrationFile(_config, health);

        await _pages.PostToPageAsync(new
        {
            type = "apiInfo",
            url = snap.Chat2ApiBaseUrl,
            model = _config.Model,
            loggedIn,
            workMode = _config.DefaultWorkMode,
            agentStrategy = _config.DefaultAgentStrategy,
            hint = loggedIn
                ? "网页会话已接入 Chat2API → DeepSeek-TUI，可使用 deepseek-v4-pro 等官方模型 ID"
                : "请先在普通对话登录 DeepSeek 网页",
            agentEngine = "deepseek-tui",
            provider = new
            {
                snap.Name,
                snap.Type,
                snap.Online,
                snap.AuthType,
                snap.ModelCount,
                snap.AccountOnline,
                snap.AccountTotal
            },
            chat2api = new
            {
                baseUrl = snap.Chat2ApiBaseUrl,
                apiKey = snap.ApiKeyForClients,
                apiKeyMasked = snap.ApiKeyMasked,
                authType = snap.AuthType
            },
            deepseekTui = new
            {
                runtimeUrl = snap.TuiRuntimeUrl,
                configPath = snap.TuiConfigPath,
                integrationFile = snap.IntegrationFilePath
            }
        });
    }

    private async void OnWebMessage(object? sender, JsonElement msg)
    {
        if (!msg.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();

        switch (type)
        {
            case "nativeReady":
                await RefreshLoginStateAsync();
                if (!string.IsNullOrWhiteSpace(_config.WebUserToken) && _mcpHub.ConnectedCount == 0)
                    _ = ConnectEnabledMcpServersAsync();
                break;
            case "syncToken":
                if (msg.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
                {
                    var t = tok.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                        await ApplyWebUserTokenAsync(t);
                }
                break;
            case "refreshLoginState":
                await RefreshLoginStateAsync();
                break;
            case "openSettings":
                Application.Current.Dispatcher.Invoke(() => OpenSettings());
                break;
            case "openChat2Api":
                Application.Current.Dispatcher.Invoke(OpenChat2Api);
                break;
            case "openAgentWorkspace":
                Application.Current.Dispatcher.Invoke(EnsureAgentWindow);
                break;
            case "showProviderCard":
                await NotifyApiInfoAsync();
                {
                    var snap = Chat2ApiProviderService.Build(_config);
                    await _pages.PostToPageAsync(new
                    {
                        type = "showProviderCard",
                        url = snap.Chat2ApiBaseUrl,
                        apiKey = snap.ApiKeyForClients,
                        apiKeyMasked = snap.ApiKeyMasked,
                        authType = snap.AuthType,
                        modelCount = snap.ModelCount,
                        tuiRuntimeUrl = snap.TuiRuntimeUrl,
                        loggedIn = snap.Online
                    });
                }
                break;
            case "setWorkMode":
                if (msg.TryGetProperty("mode", out var modeEl))
                {
                    var mode = modeEl.GetString();
                    if (mode is "chat" or "agent" or "plan")
                    {
                        var toAgent = mode is "agent" or "plan";
                        await ApplyTokenFromMessageAsync(msg);
                        if (toAgent && !_pages.IsAgentVisible)
                            await SyncTokenFromPageAsync();

                        _config.DefaultWorkMode = mode;
                        if (mode == "plan")
                            _config.DefaultAgentStrategy = AgentStrategies.Plan;
                        else if (mode == "agent")
                            _config.DefaultAgentStrategy = AgentStrategies.React;
                        ConfigStore.Save(_config);
                        _localApi.UpdateConfig(_config);

                        var targetUrl = mode == "chat"
                            ? AppNavigation.DeepSeekUrl
                            : AppNavigation.AgentPageUrl;
                        var skipNavigate = msg.TryGetProperty("skipNavigate", out var snEl)
                                           && snEl.ValueKind == JsonValueKind.True;

                        if (NavigateToUrl is not null && !skipNavigate)
                            await NavigateForWorkModeAsync(targetUrl, mode);
                        else if (mode == "chat")
                            await RunOnUiAsync(() => _pages.TriggerChatInjectAsync(forceReset: false));

                        await RefreshLoginStateAsync();
                        if (toAgent && !skipNavigate)
                            _ = PushLoginStateAfterAgentNavAsync();
                    }
                }
                break;
            case "navigateToAgent":
                await ApplyTokenFromMessageAsync(msg);
                await SyncTokenFromChatPageAsync();
                _config.DefaultWorkMode = "agent";
                ConfigStore.Save(_config);
                if (NavigateToUrl is not null)
                    await NavigateForWorkModeAsync(AppNavigation.AgentPageUrl, "agent");
                await RefreshLoginStateAsync();
                _ = PushLoginStateAfterAgentNavAsync();
                break;
            case "navigateToChat":
                _config.DefaultWorkMode = "chat";
                ConfigStore.Save(_config);
                if (NavigateToUrl is not null)
                    await NavigateForWorkModeAsync(AppNavigation.DeepSeekUrl, "chat");
                break;
            case "agentRun":
                var text = msg.TryGetProperty("text", out var tEl) ? tEl.GetString() : "";
                if (string.IsNullOrWhiteSpace(text)) return;
                var chatMode = msg.TryGetProperty("mode", out var mEl) ? mEl.GetString() : "专家";
                var deepThink = !msg.TryGetProperty("deepThink", out var dEl) || dEl.GetBoolean();
                var smartSearch = !msg.TryGetProperty("smartSearch", out var sEl) || sEl.GetBoolean();
                var mcpOn = !msg.TryGetProperty("mcpOn", out var mcEl) || mcEl.GetBoolean();
                var strategy = msg.TryGetProperty("strategy", out var stEl)
                    ? stEl.GetString()
                    : _config.DefaultAgentStrategy;
                var refIds = new List<string>();
                if (msg.TryGetProperty("refFileIds", out var rfEl) && rfEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in rfEl.EnumerateArray())
                    {
                        var id = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
                        if (!string.IsNullOrWhiteSpace(id))
                            refIds.Add(id.Trim().Trim('"'));
                    }
                }

                var sessionId = msg.TryGetProperty("sessionId", out var sidEl) ? sidEl.GetString() : null;
                var tuiThreadId = msg.TryGetProperty("tuiThreadId", out var ttEl) ? ttEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(tuiThreadId) && !string.IsNullOrWhiteSpace(sessionId))
                {
                    var stored = _agentSessions.Load(sessionId!);
                    tuiThreadId = stored?.TuiThreadId;
                }

                _ = RunAgentAsync(
                    text!, chatMode ?? "专家", deepThink, smartSearch, mcpOn,
                    strategy ?? AgentStrategies.React, refIds, sessionId, tuiThreadId);
                break;
            case "agentStop":
                _runCts?.Cancel();
                break;
            case "agentStorageList":
                await HandleAgentStorageListAsync(msg);
                break;
            case "agentStorageLoad":
                await HandleAgentStorageLoadAsync(msg);
                break;
            case "agentStorageSave":
                await HandleAgentStorageSaveAsync(msg);
                break;
            case "agentStorageDelete":
                await HandleAgentStorageDeleteAsync(msg);
                break;
            case "agentStorageCleanup":
                await HandleAgentStorageCleanupAsync(msg);
                break;
            case "agentStorageMigrate":
                await HandleAgentStorageMigrateAsync(msg);
                break;
        }
    }

    private static string? GetReqId(JsonElement msg) =>
        msg.TryGetProperty("reqId", out var r) ? r.GetString() : null;

    private Task PostStorageReplyAsync(string? reqId, object payload) =>
        _pages.PostToPageAsync(MergeReqId(reqId, payload));

    private static object MergeReqId(string? reqId, object payload)
    {
        if (string.IsNullOrEmpty(reqId)) return payload;
        var json = JsonSerializer.Serialize(payload, AgentSessionJson.Options);
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?> { ["reqId"] = reqId };
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => JsonSerializer.Deserialize<object>(prop.Value.GetRawText())
            };
        }
        return dict;
    }

    private async Task HandleAgentStorageListAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        var (bytes, count) = _agentSessions.GetStats();
        var metas = _agentSessions.ListMetas();
        await PostStorageReplyAsync(reqId, new
        {
            type = "agentStorageList",
            sessions = metas,
            totalBytes = bytes,
            count,
            storagePath = _agentSessions.StorageDirectory,
            retentionDays = _config.AgentSessionRetentionDays,
            maxStorageGb = _config.AgentSessionMaxStorageGb
        });
    }

    private async Task HandleAgentStorageLoadAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var session = string.IsNullOrWhiteSpace(id) ? null : _agentSessions.Load(id!);
        await PostStorageReplyAsync(reqId, new { type = "agentStorageLoad", session });
    }

    private async Task HandleAgentStorageSaveAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        AgentSessionFile? session = null;
        if (msg.TryGetProperty("session", out var sEl))
            session = JsonSerializer.Deserialize<AgentSessionFile>(sEl.GetRawText(), AgentSessionJson.Options);

        if (session is not null)
        {
            _agentSessions.Save(session);
            if (_config.AgentSessionAutoCleanup)
                _agentSessions.ApplyRetentionPolicy(_config);
        }

        var (bytes, count) = _agentSessions.GetStats();
        await PostStorageReplyAsync(reqId, new
        {
            type = "agentStorageSave",
            ok = session is not null,
            totalBytes = bytes,
            count
        });
    }

    private async Task HandleAgentStorageDeleteAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        var ids = new List<string>();
        if (msg.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var id = item.GetString();
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
            }
        }

        _agentSessions.Delete(ids);
        var (bytes, count) = _agentSessions.GetStats();
        await PostStorageReplyAsync(reqId, new
        {
            type = "agentStorageDelete",
            deleted = ids,
            sessions = _agentSessions.ListMetas(),
            totalBytes = bytes,
            count
        });
    }

    private async Task HandleAgentStorageCleanupAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        var deleted = _agentSessions.ApplyRetentionPolicy(_config);
        var (bytes, count) = _agentSessions.GetStats();
        await PostStorageReplyAsync(reqId, new
        {
            type = "agentStorageCleanup",
            deleted,
            sessions = _agentSessions.ListMetas(),
            totalBytes = bytes,
            count
        });
    }

    private async Task HandleAgentStorageMigrateAsync(JsonElement msg)
    {
        var reqId = GetReqId(msg);
        var imported = 0;
        if (msg.TryGetProperty("sessions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<AgentSessionFile>();
            foreach (var item in arr.EnumerateArray())
            {
                var s = JsonSerializer.Deserialize<AgentSessionFile>(item.GetRawText(), AgentSessionJson.Options);
                if (s is not null) list.Add(s);
            }

            _agentSessions.ImportLegacySessions(list);
            imported = list.Count;
        }

        var (bytes, count) = _agentSessions.GetStats();
        await PostStorageReplyAsync(reqId, new
        {
            type = "agentStorageMigrate",
            imported,
            sessions = _agentSessions.ListMetas(),
            totalBytes = bytes,
            count
        });
    }

    public async Task SyncTokenFromChatPageAsync()
    {
        if (_pages.IsAgentVisible)
            return;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            await SyncTokenFromPageAsync();
            ReloadConfig();
            if (!string.IsNullOrWhiteSpace(_config.WebUserToken))
                return;
            if (attempt < 9)
                await Task.Delay(attempt < 3 ? 60 : 150);
        }
    }

    public async Task OnChatNavigationCompletedAsync() =>
        await RefreshLoginStateAsync();

    public async Task OnAgentNavigationCompletedAsync()
    {
        await RefreshLoginStateAsync();
        _ = PushLoginStateAfterAgentNavAsync();
    }

    private async Task PushLoginStateAfterAgentNavAsync()
    {
        foreach (var delay in new[] { 0, 120, 400, 900 })
        {
            if (delay > 0)
                await Task.Delay(delay);
            if (!_pages.IsAgentVisible)
                return;
            await PushLoginStateAsync();
            await NotifyApiInfoAsync();
        }
    }

    public async Task RefreshLoginStateAsync()
    {
        ReloadConfig();
        // 先用已保存的 token 更新 Agent 页，避免桥接未就绪时长期停在「检测中…」
        await PushLoginStateAsync();
        await NotifyApiInfoAsync();

        _ = RefreshLoginStateBackgroundAsync();
    }

    private async Task RefreshLoginStateBackgroundAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_config.WebUserToken))
                await _pages.SyncApiBridgeTokenAsync(_config.WebUserToken);

            await SyncTokenFromBridgeOrChatAsync();
            ReloadConfig();
            await PushLoginStateAsync();
            await NotifyApiInfoAsync();
        }
        catch
        {
            // 后台同步失败不影响已展示的登录状态
        }
    }

    private async Task SyncTokenFromBridgeOrChatAsync()
    {
        try
        {
            var token = await _pages.TryReadUserTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
                return;
            var normalized = NormalizeUserToken(token);
            if (string.IsNullOrWhiteSpace(normalized) || normalized == _config.WebUserToken)
                return;
            await ApplyWebUserTokenAsync(normalized);
        }
        catch
        {
            // 页面或桥接尚未就绪
        }
    }

    public async Task PushLoginStateToPageAsync() => await PushLoginStateAsync();

    private async Task PushLoginStateAsync()
    {
        ReloadConfig();
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);
        await _pages.PushAgentAuthHintAsync(loggedIn);
        await _pages.PostToPageAsync(new { type = "loginState", loggedIn });
    }

    private async Task ApplyTokenFromMessageAsync(JsonElement msg)
    {
        if (!msg.TryGetProperty("token", out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String)
            return;
        var token = tokenEl.GetString();
        if (!string.IsNullOrWhiteSpace(token))
            await ApplyWebUserTokenAsync(token);
    }

    private async Task ApplyWebUserTokenAsync(string token)
    {
        var normalized = NormalizeUserToken(token);
        if (string.IsNullOrWhiteSpace(normalized))
            return;
        _config.WebUserToken = normalized;
        ConfigStore.Save(_config);
        await Chat2ApiStackBootstrap.OnWebLoginAsync(_config, _localApi, _tuiHost, _pages.Chat);
        await NotifyApiInfoAsync();
        await PushLoginStateAsync();
    }

    private static string? NormalizeUserToken(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return doc.RootElement.GetString();
        }
        catch
        {
            // plain token string
        }

        return trimmed.Trim('"');
    }

    private async Task SyncTokenFromPageAsync()
    {
        try
        {
            if (_pages.IsAgentVisible)
                return;

            var raw = await _pages.GetUserTokenAsync();
            if (string.IsNullOrWhiteSpace(raw))
                return;
            var token = NormalizeUserToken(raw);
            if (string.IsNullOrWhiteSpace(token))
                return;
            if (token == _config.WebUserToken)
                return;
            await ApplyWebUserTokenAsync(token);
        }
        catch
        {
            // page may not be ready
        }
    }

    private void OpenChat2Api()
    {
        ReloadConfig();
        var dlg = new Chat2ApiManagementWindow(_config, c =>
        {
            _config = c;
            ConfigStore.Save(_config);
            _localApi.UpdateConfig(_config);
        })
        { Owner = _owner };
        dlg.ShowDialog();
        ReloadConfig();
        _localApi.UpdateConfig(_config);
        _ = NotifyApiInfoAsync();
    }

    private void OpenSettings()
    {
        var dlg = new DesktopSettingsWindow(_config, _mcpHub) { Owner = _owner };
        var saved = dlg.ShowDialog() == true;
        if (saved && dlg.Config is not null)
        {
            _config = dlg.Config;
            ConfigStore.Save(_config);
        }
        else
            ReloadConfig();

        _localApi.UpdateConfig(_config);
        _ = NotifyApiInfoAsync();
    }

    private void EnsureAgentWindow()
    {
        if (_agentWindow is { IsLoaded: true })
        {
            _agentWindow.Show();
            _agentWindow.Activate();
            return;
        }

        _agentWindow = new AgentRunWindow { Owner = _owner };
        _agentWindow.StopRequested += (_, _) => _runCts?.Cancel();
        _agentWindow.Closed += (_, _) => _agentWindow = null;
        _agentWindow.Show();
    }

    private async Task RunAgentAsync(
        string task, string mode, bool deepThink, bool smartSearch, bool mcpOn, string strategy,
        IReadOnlyList<string>? refFileIds = null,
        string? sessionId = null,
        string? tuiThreadId = null)
    {
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        if (_agentWindow is { IsLoaded: true })
        {
            _agentWindow.ClearLog();
            _agentWindow.SetTask(task);
            _agentWindow.SetRunning(true);
        }

        ReloadConfig();
        await SyncTokenFromPageAsync();
        AgentModeHelper.ApplyAgentDefaults(_config);
        AgentModeHelper.ApplyChatMode(_config, "专家", deepThink: true);
        deepThink = true;
        smartSearch = true;
        _pages.AgentRefFileIds = refFileIds ?? Array.Empty<string>();
        ConfigStore.Save(_config);
        _localApi.UpdateConfig(_config);

        void Log(string line)
        {
            Application.Current.Dispatcher.Invoke(() => _agentWindow?.AppendLog(line));
            _ = _pages.PostToPageAsync(new { type = "agentLog", text = line });
            if (line.StartsWith("Final Answer: ", StringComparison.OrdinalIgnoreCase))
            {
                var ans = line["Final Answer: ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(ans))
                    _ = _pages.PostToPageAsync(new { type = "agentAnswer", text = ans });
            }
        }

        try
        {
            await _pages.PostToPageAsync(new { type = "agentStarted", task });

            if (string.IsNullOrWhiteSpace(_config.WebUserToken))
            {
                Log("请先在网页登录 DeepSeek。");
                return;
            }

            Log($"DeepSeek-TUI → Chat2API {_localApi.BaseUrl}");
            Log($"工作区: {AgentWorkspace.ResolveRoot(_config)}");
            Log("MCP / Skills / 沙箱工具由 DeepSeek-TUI 管理（~/.deepseek/）");

            var result = await GetTuiAgent().RunAsync(
                _config, task, strategy, tuiThreadId, Log,
                delta =>
                {
                    if (!string.IsNullOrEmpty(delta))
                        _ = _pages.PostToPageAsync(new { type = "agentAnswer", text = delta, append = true });
                },
                ct);

            var answer = result.Answer;
            var summary = answer;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var sess = _agentSessions.Load(sessionId) ?? new AgentSessionFile
                {
                    Id = sessionId,
                    Title = "新对话",
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                };
                sess.TuiThreadId = result.ThreadId;
                sess.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _agentSessions.Save(sess);
            }

            await _pages.PostToPageAsync(new
            {
                type = "agentTuiThread",
                sessionId,
                tuiThreadId = result.ThreadId
            });

            await _pages.PostToPageAsync(new { type = "agentDone", summary, answer });
        }
        catch (OperationCanceledException)
        {
            Log("已停止。");
            await _pages.PostToPageAsync(new { type = "agentDone", summary = "已停止", answer = "" });
        }
        catch (Exception ex)
        {
            Log("错误: " + ex.Message);
            await _pages.PostToPageAsync(new { type = "agentDone", summary = "失败: " + ex.Message });
        }
        finally
        {
            _pages.AgentRefFileIds = Array.Empty<string>();
            _agentWindow?.SetRunning(false);
        }
    }

    private async Task ConnectMcpAsync(Action<string> onLog, CancellationToken ct)
    {
        await _mcpHub.DisconnectAllAsync(ct);
        var errors = await _mcpHub.ConnectEnabledAsync(_config.McpServers, onLog, ct);
        onLog($"已连接 {_mcpHub.ConnectedCount} 个 MCP 服务");
        foreach (var err in errors)
            onLog("连接失败: " + err);
    }

    public async ValueTask DisposeAsync()
    {
        _pages.MessageReceived -= OnWebMessage;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        await _mcpHub.DisposeAsync();
        await _tuiHost.DisposeAsync();
    }
}
