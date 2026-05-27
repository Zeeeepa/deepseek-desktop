using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.AppLayer.Agent;
using DeepSeekBrowser.AppLayer.Embedded;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.Harness;
using DeepSeekBrowser.Services.Harness.Graph;
using DeepSeekBrowser.Services.Harness.Interop;
using DeepSeekBrowser.Services.Harness.Memory;
using DeepSeekBrowser.Services.Harness.Observability;
using DeepSeekBrowser.Services.Harness.Sandbox;
using DeepSeekBrowser.Services.Harness.Workers;
using DeepSeekBrowser.Views;

namespace DeepSeekBrowser.Services;

public sealed partial class DesktopAgentHost : IAsyncDisposable
{
    private readonly McpHub _mcpHub = new();
    private readonly IDesktopWebHost _pages;

    private WebInjectService ChatWebInject => (WebInjectService)_pages.Chat;
    private readonly LocalOpenAiServer _localApi;
    private AppConfig _config = new();
    private CancellationTokenSource? _runCts;
    private readonly SemaphoreSlim _workModeGate = new(1, 1);
    private Window? _owner;
    private AgentRunWindow? _agentWindow;
    private DsdApiIpcBridge? _dsdApiIpc;
    private readonly AgentWebViewCoordinator _embedCoordinator = new();
    private readonly EmbeddedPanelRegistry _embeddedPanels = new();
    private readonly AgentSessionOrchestrator _sessionOrchestrator = new();
    private EmbeddedSettingsSupport? _embeddedSettings;
    private readonly AgentSessionStore _agentSessions = new();
    private DeepSeekHarnessRunner? _harnessRunner;
    private HarnessWorkerProcessPool? _workerPool;
    private string? _lastAgentWebChatSessionId;
    private AgentAutomationHost? _automationHost;
    private AgentAutomationSupport? _automationSupport;
    private TaskCompletionSource<string>? _userQuestionTcs;
    private TaskCompletionSource<bool>? _permissionTcs;

    public Func<string, Task>? NavigateToUrl { get; set; }

    /// <summary>为 false 时不向 WebView Agent 页 postMessage（WinUI 原生 Agent 使用）。</summary>
    public bool PostToWebUi { get; set; } = true;

    public event EventHandler<AgentMessageEventArgs>? OnMessage;
    public event EventHandler<AgentStreamDeltaEventArgs>? OnStreamDelta;
    public event EventHandler<AgentRunStateEventArgs>? OnRunStateChanged;

    private static Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = Application.Current.Dispatcher;
        return dispatcher.CheckAccess()
            ? action()
            : dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void RememberWebChatSessionFromMessage(JsonElement msg)
    {
        if (msg.ValueKind != JsonValueKind.Object) return;
        if (!msg.TryGetProperty("webChatSessionId", out var el) || el.ValueKind != JsonValueKind.String) return;
        RememberWebChatSession(el.GetString());
    }

    private void RememberWebChatSession(string? sessionId)
    {
        var id = (sessionId ?? "").Trim();
        if (string.IsNullOrEmpty(id)) return;
        _lastAgentWebChatSessionId = id;
    }

    private string ResolveChatNavigationUrl() =>
        AppNavigation.ChatSessionUrl(_lastAgentWebChatSessionId);

    public DesktopAgentHost(IDesktopWebHost pages, LocalOpenAiServer localApi)
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
        RequestToolApprovalAsync(toolName, detail, "执行工具", "DeepSeek Agent 工具审批");

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

    private HarnessWorkerProcessPool GetWorkerPool()
    {
        if (_workerPool is null && _config.AgentWorkerEnabled)
            _workerPool = new HarnessWorkerProcessPool(_config);
        return _workerPool!;
    }

    private DeepSeekHarnessRunner GetHarnessRunner() =>
        _harnessRunner ??= new DeepSeekHarnessRunner(
            () => AgentChatClientFactory.Create(_config, new DesktopWebChatAdapter(_pages)),
            _mcpHub,
            RequestToolApprovalAsync,
            new DesktopUserQuestionHandler(this),
            RequestScopeApprovalAsync,
            _config.AgentWorkerEnabled ? GetWorkerPool() : null);

    private sealed class DesktopUserQuestionHandler : IUserQuestionHandler
    {
        private readonly DesktopAgentHost _host;

        public DesktopUserQuestionHandler(DesktopAgentHost host) => _host = host;

        public Task<string> AskAsync(UserQuestionRequest request, CancellationToken ct) =>
            _host.RequestUserQuestionAsync(request, ct);
    }

    /// <summary>供 DEEPSEEK_DESKTOP_VERIFY_WORKMODE=1 自检切换链路。</summary>
    public void Start()
    {
        ReloadConfig();
        DsdOpenAiCompat.EnsureDefaultMappings(_config);
        _localApi.UpdateConfig(_config);
        if (_config.EnableExternalOpenAiApi)
            _localApi.EnsureExternalApiListening();
        _ = ConnectEnabledMcpServersAsync();
        _ = EnsureEmbeddedStackLinkedAsync();
        StartAutomations();
    }

    private void StartAutomations()
    {
        _automationHost?.Dispose();
        _automationHost = new AgentAutomationHost(_config, ExecuteAutomationAsync, OnAutomationRunUpdated);
        _automationSupport = new AgentAutomationSupport(
            _automationHost,
            payload => _pages.Agent.PostToPageAsync(payload));
        _automationHost.Start();
    }

    private void OnAutomationRunUpdated(AgentAutomationRun run) =>
        _ = _pages.Agent.PostToPageAsync(new { type = "agentAutomationRun", run });

    /// <summary>启动后自动联通内嵌对话服务与 DeepSeek-TUI（无需手动启代理）。</summary>
    public Task EnsureEmbeddedStackLinkedAsync(CancellationToken ct = default)
    {
        ReloadConfig();
        return EmbeddedStackCoordinator.EnsureLinkedAsync(_config, _localApi, ChatWebInject, ct);
    }

    public async Task WarmDsdApiBridgeAsync()
    {
        ReloadConfig();
        await SyncApiBridgeFromApiAccountsAsync();
        try
        {
            await _pages.EnsureApiBridgeReadyAsync();
        }
        catch
        {
            // 启动时网络未就绪可忽略，发消息前会再次探测
        }
    }

    private async Task<IReadOnlyList<string>> ConnectEnabledMcpServersAsync()
    {
        var servers = McpConfigInterop.MergeEnabledServers(_config);
        return await _mcpHub.ConnectEnabledAsync(servers, _ => { }, CancellationToken.None);
    }

    public void ReloadConfig()
    {
        _config = ConfigStore.Load();
        _localApi.UpdateConfig(_config);
    }

    private static bool HasConfiguredDeepSeekApiAccount(AppConfig config) =>
        ProviderAccountStore.ByProvider("deepseek").Any(a =>
            a.Status == "active"
            && !string.IsNullOrWhiteSpace(AccountCredentials.ResolveWebUserToken(a, config)));

    public async Task NotifyApiInfoAsync()
    {
        ReloadConfig();
        var apiToken = AccountCredentials.ResolveFirstProviderWebToken("deepseek", _config);
        var loggedIn = !string.IsNullOrWhiteSpace(apiToken);
        DsdApiHealth? health = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(apiToken))
                health = await _pages.ProbeDsdApiHealthAsync(apiToken, InternalChatChannel.DesktopV1);
        }
        catch
        {
            // ignore
        }

        DsdApiProviderService.WriteIntegrationFile(_config, health);

        await _pages.PostToPageAsync(new
        {
            type = "loginState",
            loggedIn,
            workMode = _config.DefaultWorkMode,
            agentStrategy = _config.DefaultAgentStrategy,
            agentDeepThinking = _config.AgentDeepThinking,
            agentWebSearch = _config.AgentWebSearch,
            agentModelAuto = _config.AgentModelAuto,
            agentManualModel = _config.AgentManualModel,
            agentManualProviderId = _config.AgentManualProviderId,
            agentAutoPreferProviderOrder = _config.AgentAutoPreferProviderOrder,
            agentAutoProviderOrder = _config.AgentAutoProviderOrder,
            agentDefaultProviderId = _config.AgentDefaultProviderId,
            agentModel = _config.Model,
            agentThinkingDisplayMode = _config.AgentThinkingDisplayMode ?? AgentThinkingDisplayModes.Normal,
            agentInferenceMode = _config.AgentInferenceMode ?? AgentInferenceModes.Web,
            agentSessionPlan = _config.AgentSessionPlanMarkdown ?? "",
            hint = loggedIn
                ? "API 账户已配置，可使用 Agent"
                : "请在 API 管理中手动添加 DeepSeek 账户",
            agentEngine = "native-harness"
        });
    }

    private async void OnWebMessage(object? sender, JsonElement msg)
    {
        if (!msg.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();
        if (type is "setWorkMode" or "toggleWorkMode")
            WorkModeTrace.Write("OnWebMessage " + msg.GetRawText());

        switch (type)
        {
            case "nativeReady":
                await _pages.WorkMode.BroadcastImmediateAsync();
                _pages.WorkMode.ScheduleBroadcastRetries();
                if (_pages.Chat is WebInjectService chatInject)
                    _ = chatInject.EnsureChatModeFloaterAsync();
                _ = RefreshLoginStateBackgroundAsync();
                var apiToken = AccountCredentials.ResolveWebUserTokenForRoute(null, _config, "deepseek");
                if (!string.IsNullOrWhiteSpace(apiToken) && _mcpHub.ConnectedCount == 0)
                    _ = ConnectEnabledMcpServersAsync();
                break;
            case "syncToken":
                // 已废弃：Agent 不从普通对话 localStorage 同步 Token
                break;
            case "refreshLoginState":
                await RefreshLoginStateAsync();
                break;
            case "prepareEmbeddedPanel":
                ReloadConfig();
                GetDsdApiIpc().RefreshConfig(_config);
                break;
            case "openSettings":
                await EnsureAgentAndShowEmbeddedPanelAsync("settings");
                break;
            case "openApiManagement":
                await EnsureAgentAndShowEmbeddedPanelAsync("apiManagement");
                break;
            case "ipcInvoke":
                await HandleEmbeddedIpcInvokeAsync(msg);
                break;
            case "consoleUiReady":
                await _pages.Agent.PostToPageAsync(new { type = "embeddedPanelReady", panel = "apiManagement" });
                break;
            case "openDeepSeekLogin":
                if (NavigateToUrl is not null)
                    await NavigateToUrl(AppNavigation.DeepSeekUrl);
                await RefreshLoginStateAsync();
                break;
            case "syncDesktopStack":
                ReloadConfig();
                GetDsdApiIpc().RefreshConfig(_config);
                await SyncDsdApiStackAsync(_config, CancellationToken.None);
                await _pages.Agent.PostToPageAsync(new { type = "desktopStackSynced", ok = true });
                break;
            case "openTuiConfigFile":
            case "openAgentConfigFile":
                OpenAgentConfigFile();
                break;
            case "openAgentFromApiManagement":
                if (!_pages.IsAgentVisible)
                {
                    await _pages.WorkMode.ShowAgentSurfaceAsync();
                    await _pages.WorkMode.BroadcastImmediateAsync();
                }
                await _pages.Agent.PostToPageAsync(new { type = "hideEmbeddedPanel" });
                break;
            case "settingsLoad":
            case "settingsSave":
            case "settingsConnectAllMcp":
            case "settingsMcpToggle":
            case "settingsMcpAdd":
            case "settingsMcpEdit":
            case "settingsMcpRemove":
            case "settingsRunDoctor":
            case "settingsOpenDeepSeekHome":
            case "settingsOpenDocs":
            case "settingsCopyConfigPath":
            case "settingsOpenConfig":
            case "settingsTestLangfuse":
                await GetEmbeddedSettings().HandleAsync(msg);
                if (type == "settingsSave")
                {
                    _localApi.UpdateConfig(_config);
                    GetDsdApiIpc().RefreshConfig(_config);
                    await NotifyApiInfoAsync();
                }
                break;
            case "openAgentWorkspace":
                Application.Current.Dispatcher.Invoke(EnsureAgentWindow);
                break;
            case "agentWorkspaceGet":
            case "agentWorkspaceSet":
            case "agentWorkspacePickFolder":
            case "agentWorkspacePatch":
                await HandleAgentWorkspaceAsync(msg, type);
                break;
            case "agentProviderCatalog":
                await HandleAgentProviderCatalogAsync(msg);
                break;
            case "showProviderCard":
                await NotifyApiInfoAsync();
                break;
            case "requestWorkModeState":
                await _pages.WorkMode.BroadcastImmediateAsync();
                break;
            case "toggleWorkMode":
                await ApplyWorkModeAsync(msg, _pages.WorkMode.ToggleTargetMode());
                break;
            case "setWorkMode":
                if (msg.TryGetProperty("mode", out var modeEl))
                {
                    var mode = modeEl.GetString();
                    if (mode is "chat" or "agent" or "plan")
                        await ApplyWorkModeAsync(msg, mode);
                }
                break;
            case "navigateToAgent":
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
                var webChatSessionIdHint = msg.TryGetProperty("webChatSessionId", out var wcsEl)
                    ? wcsEl.GetString()
                    : null;
                var harnessState = msg.TryGetProperty("harnessState", out var hsEl)
                    ? hsEl.GetString()
                    : (msg.TryGetProperty("tuiThreadId", out var ttEl) ? ttEl.GetString() : null);
                var playbookId = msg.TryGetProperty("playbookId", out var pbEl) ? pbEl.GetString() : null;
                var skillId = msg.TryGetProperty("skillId", out var skEl) ? skEl.GetString() : null;
                if (msg.TryGetProperty("modelAuto", out var modelAutoEl))
                    _config.AgentModelAuto = modelAutoEl.GetBoolean();
                if (msg.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
                {
                    var manual = modelEl.GetString();
                    if (!string.IsNullOrWhiteSpace(manual))
                        _config.AgentManualModel = manual.Trim();
                }
                if (msg.TryGetProperty("providerId", out var provEl) && provEl.ValueKind == JsonValueKind.String)
                {
                    var pid = provEl.GetString();
                    if (!string.IsNullOrWhiteSpace(pid))
                        _config.AgentManualProviderId = pid.Trim();
                }

                _ = RunAgentAsync(
                    text!, chatMode ?? "专家", deepThink, smartSearch, mcpOn,
                    strategy ?? _config.DefaultAgentStrategy ?? AgentStrategies.Execute, refIds, sessionId, harnessState, playbookId, skillId, webChatSessionIdHint);
                break;
            case "agentStop":
                CancelAgentRun();
                break;
            case "agentPatchResolve":
                HandleAgentPatchResolve(msg);
                break;
            case "agentIndexStatus":
                await HandleAgentIndexStatusAsync(msg);
                break;
            case "agentUndoSession":
                await HandleAgentUndoSessionAsync(msg);
                break;
            case "setAgentFeatures":
                if (msg.TryGetProperty("deepThink", out var dtEl))
                    _config.AgentDeepThinking = dtEl.GetBoolean();
                if (msg.TryGetProperty("smartSearch", out var ssEl))
                    _config.AgentWebSearch = ssEl.GetBoolean();
                ConfigStore.Save(_config);
                AgentDesktopConfigSync.Apply(_config);
                await NotifyApiInfoAsync();
                break;
            case "agentSessionList":
            case "agentSessionLoad":
            case "agentSessionSave":
            case "agentSessionDelete":
            case "agentSessionRename":
            case "agentSessionPin":
                await HandleAgentSessionAsync(msg, type);
                break;
            case "agentAutomationsBootstrap":
            case "agentAutomationsList":
            case "agentAutomationsRuns":
            case "agentAutomationsSave":
            case "agentAutomationsDelete":
            case "agentAutomationsToggle":
            case "agentAutomationsTest":
                if (_automationSupport is not null)
                    await _automationSupport.HandleAsync(msg, type);
                break;
            case "agentPlaybooksList":
                await HandleAgentPlaybooksAsync(msg);
                break;
            case "agentCheckpointGet":
                await HandleAgentCheckpointAsync(msg);
                break;
            case "agentSkillsList":
                await HandleAgentSkillsAsync(msg);
                break;
            case "agentHarnessReload":
                await HandleAgentHarnessReloadAsync(msg);
                break;
            case "agentAskUserResponse":
                HandleAgentAskUserResponse(msg);
                break;
            case "agentMcpStatus":
                await HandleAgentMcpStatusAsync(msg);
                break;
            case "agentInitAgents":
                await HandleAgentInitAgentsAsync(msg);
                break;
            case "agentUndoFile":
                await HandleAgentUndoFileAsync(msg);
                break;
            case "agentUndoList":
                await HandleAgentUndoListAsync(msg);
                break;
            case "agentUndoRestore":
                await HandleAgentUndoRestoreAsync(msg);
                break;
            case "agentPermissionResponse":
                HandleAgentPermissionResponse(msg);
                break;
            case "agentWorkspaceFiles":
                await HandleAgentWorkspaceFilesAsync(msg);
                break;
            case "agentNormalizePaths":
                await HandleAgentNormalizePathsAsync(msg);
                break;
            case "agentPathPicker":
                await HandleAgentPathPickerAsync(msg);
                break;
            case "agentPickFolder":
                await HandleAgentPickFolderAsync(msg);
                break;
            case "agentPickReferences":
                await HandleAgentPickReferencesAsync(msg);
                break;
            case "agentWorkspaceReadSnippet":
                await HandleAgentWorkspaceReadSnippetAsync(msg);
                break;
            case "agentPlanGet":
                await HandleAgentPlanGetAsync(msg);
                break;
            case "agentRunsList":
                await HandleAgentRunsListAsync(msg);
                break;
            case "agentRunLoad":
                await HandleAgentRunLoadAsync(msg);
                break;
            case "agentMemorySearch":
                await HandleAgentMemorySearchAsync(msg);
                break;
            case "agentMemoryForget":
                await HandleAgentMemoryForgetAsync(msg);
                break;
            case "agentMemoryClearSession":
                await HandleAgentMemoryClearSessionAsync(msg);
                break;
            case "agentGraphList":
                await HandleAgentGraphListAsync(msg);
                break;
            case "agentResumeThread":
                await HandleAgentResumeThreadAsync(msg);
                break;
            case "agentSkillsReindex":
                await HandleAgentSkillsReindexAsync(msg);
                break;
            case "agentSkillsSearch":
                await HandleAgentSkillsSearchAsync(msg);
                break;
            case "agentBlocksList":
                await HandleAgentBlocksListAsync(msg);
                break;
        }
    }

    private void HandleAgentAskUserResponse(JsonElement msg)
    {
        var answer = msg.TryGetProperty("answer", out var aEl) ? aEl.GetString() ?? "" : "";
        _userQuestionTcs?.TrySetResult(string.IsNullOrWhiteSpace(answer) ? "继续" : answer);
        _userQuestionTcs = null;
    }

    private void HandleAgentPermissionResponse(JsonElement msg)
    {
        var allow = msg.TryGetProperty("allow", out var aEl) && aEl.GetBoolean();
        if (allow && msg.TryGetProperty("rememberScopes", out var rsEl) && rsEl.ValueKind == JsonValueKind.Object)
        {
            ReloadConfig();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_config.AgentPermissionScopesJson))
            {
                try
                {
                    using var existing = JsonDocument.Parse(_config.AgentPermissionScopesJson);
                    foreach (var p in existing.RootElement.EnumerateObject())
                        dict[p.Name] = p.Value.GetString() ?? "allow";
                }
                catch { /* ignore */ }
            }

            foreach (var p in rsEl.EnumerateObject())
                dict[p.Name] = "allow";
            _config.AgentPermissionScopesJson = JsonSerializer.Serialize(dict);
            ConfigStore.Save(_config);

            var tool = msg.TryGetProperty("toolName", out var tn) ? tn.GetString() : "write";
            var workspace = _config.AgentWorkspaceRoot;
            if (!string.IsNullOrWhiteSpace(workspace))
                HarnessApprovalRulesStore.AddAlwaysAllow(workspace, tool ?? "write", "*");
        }

        _permissionTcs?.TrySetResult(allow);
        _permissionTcs = null;
    }

    private void HandleAgentPatchResolve(JsonElement msg)
    {
        var patchId = msg.TryGetProperty("patchId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(patchId)) return;
        var accepted = !msg.TryGetProperty("accepted", out var aEl) || aEl.GetBoolean();
        ReloadConfig();
        var workspace = _config.AgentWorkspaceRoot;
        if (string.IsNullOrWhiteSpace(workspace)) return;

        if (accepted)
        {
            if (HarnessPatchStaging.TryAccept(workspace, patchId, out var err))
            {
                HarnessPatchMetrics.RecordAccepted();
                _ = _pages.PostToPageAsync(new { type = "agentPatchResolved", patchId, accepted = true });
            }
            else
                _ = _pages.PostToPageAsync(new { type = "agentLog", text = "Accept patch 失败: " + err });
        }
        else
        {
            HarnessPatchStaging.TryReject(workspace, patchId);
            HarnessPatchMetrics.RecordRejected();
            _ = _pages.PostToPageAsync(new { type = "agentPatchResolved", patchId, accepted = false });
        }
    }

    private void CancelAgentRun()
    {
        _runCts?.Cancel();
        _userQuestionTcs?.TrySetCanceled();
        _permissionTcs?.TrySetCanceled();
        _pages.CancelActiveWebChatStreams();
        AgentDebugLogger.Current?.Write("AGENT", "用户请求停止");
    }

    public Task<bool> RequestScopeApprovalAsync(string toolName, string detail, IReadOnlyList<string> scopes)
    {
        if (!PostToWebUi)
            return RequestToolApprovalAsync(toolName, detail);

        _permissionTcs?.TrySetCanceled();
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _permissionTcs = tcs;
        _ = _pages.PostToPageAsync(new
        {
            type = "agentPermissionRequest",
            tool = toolName,
            detail,
            scopes
        });
        return tcs.Task;
    }

    public Task<string> RequestUserQuestionAsync(UserQuestionRequest request, CancellationToken ct)
    {
        _userQuestionTcs?.TrySetCanceled();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _userQuestionTcs = tcs;
        if (ct.CanBeCanceled)
            ct.Register(() => tcs.TrySetCanceled());
        var payload = new
        {
            type = "agentAskUser",
            question = request.Question,
            options = request.Options.Select(o => new { label = o.Label, description = o.Description }).ToList()
        };
        if (PostToWebUi)
            _ = _pages.PostToPageAsync(payload);
        else
            tcs.TrySetResult(request.Options.FirstOrDefault()?.Label ?? "继续");
        return tcs.Task;
    }

    private async Task HandleAgentMcpStatusAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        ReloadConfig();
        var servers = _config.McpServers.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            enabled = s.Enabled,
            connected = s.Enabled && _mcpHub.IsConnected(s.Id),
            toolCount = s.Enabled ? _mcpHub.GetToolCount(s.Id) : 0
        }).ToList();
        await _pages.Agent.PostToPageAsync(new { type = "agentMcpStatus", reqId, ok = true, servers });
    }

    private async Task HandleAgentInitAgentsAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var path = HarnessAgentsMdInit.WriteDefault(workspace);
        await _pages.Agent.PostToPageAsync(new { type = "agentInitAgents", reqId, ok = true, path });
    }

    private async Task HandleAgentUndoFileAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var rel = msg.TryGetProperty("path", out var pEl) ? pEl.GetString() ?? "" : "";
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var history = new HarnessFileHistory(workspace);
        var ok = !string.IsNullOrWhiteSpace(rel) && history.TryRestoreSingleFile(rel);
        await _pages.Agent.PostToPageAsync(new { type = "agentUndoFile", reqId, ok, path = rel });
    }

    private async Task HandleAgentUndoListAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var sessionId = msg.TryGetProperty("sessionId", out var sidEl) ? sidEl.GetString() ?? "" : "";
        ReloadConfig();
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var undo = new HarnessUndoService(workspace, _agentSessions);
        var targets = string.IsNullOrWhiteSpace(sessionId)
            ? Array.Empty<HarnessUndoTarget>()
            : undo.ListTargets(sessionId);
        await _pages.Agent.PostToPageAsync(new
        {
            type = "agentUndoList",
            reqId,
            ok = true,
            targets = targets.Select(t => new
            {
                messageId = t.MessageId,
                preview = t.Preview,
                canRestoreCode = t.CanRestoreCode,
                checkpointHash = t.CheckpointHash
            })
        });
    }

    private async Task HandleAgentUndoSessionAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var runId = msg.TryGetProperty("runId", out var ridEl) ? ridEl.GetString() : null;
        ReloadConfig();
        var workspace = AgentWorkspace.ResolveRoot(_config);
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(workspace))
        {
            await _pages.PostToPageAsync(new { type = "agentUndoSession", reqId, ok = false });
            return;
        }

        var store = new HarnessSessionStore(workspace);
        var ok = store.TryUndoSessionRun(runId, workspace, out var restored);
        await _pages.PostToPageAsync(new
        {
            type = "agentUndoSession",
            reqId,
            ok,
            restoredFiles = restored
        });
    }

    private async Task HandleAgentIndexStatusAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        ReloadConfig();
        var workspace = AgentWorkspace.ResolveRoot(_config);
        if (string.IsNullOrWhiteSpace(workspace))
        {
            await _pages.PostToPageAsync(new { type = "agentIndexStatus", reqId, ok = false });
            return;
        }

        var indexer = DeepSeekBrowser.Services.Index.CodebaseIndexer.GetOrCreate(workspace);
        indexer.EnsureWatching();
        var status = indexer.GetStatus();
        await _pages.PostToPageAsync(new
        {
            type = "agentIndexStatus",
            reqId,
            ok = true,
            chunkCount = status.ChunkCount,
            lastUpdatedUtc = status.LastUpdatedUtc?.ToString("O")
        });
    }

    private async Task HandleAgentUndoRestoreAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var sessionId = msg.TryGetProperty("sessionId", out var sidEl) ? sidEl.GetString() ?? "" : "";
        var messageId = msg.TryGetProperty("messageId", out var midEl) ? midEl.GetString() ?? "" : "";
        var includeCode = msg.TryGetProperty("includeCode", out var icEl) && icEl.GetBoolean();
        ReloadConfig();
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var undo = new HarnessUndoService(workspace, _agentSessions);
        var updated = undo.RestoreConversation(sessionId, messageId, includeCode);
        await _pages.Agent.PostToPageAsync(new
        {
            type = "agentUndoRestore",
            reqId,
            ok = updated is not null,
            session = updated
        });
    }

    private void ApplyCheckpointToSession(string sessionId, string checkpointHash)
    {
        var session = _agentSessions.Load(sessionId);
        if (session is null)
            return;
        for (var i = session.Messages.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(session.Messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                continue;
            session.Messages[i].CheckpointHash = checkpointHash;
            if (string.IsNullOrWhiteSpace(session.Messages[i].Id))
                session.Messages[i].Id = Guid.NewGuid().ToString("N");
            break;
        }

        session.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _agentSessions.Save(session);
    }

    private async Task HandleAgentWorkspaceFilesAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var query = msg.TryGetProperty("query", out var qEl) ? qEl.GetString() ?? "" : "";
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var files = await Task.Run(() => CollectWorkspaceFiles(workspace, query, 60)).ConfigureAwait(true);
        await _pages.Agent.PostToPageAsync(new { type = "agentWorkspaceFiles", reqId, ok = true, files });
    }

    private async Task HandleAgentNormalizePathsAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var input = new List<string>();
        if (msg.TryGetProperty("paths", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var p = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    input.Add(p);
            }
        }

        var refs = await Task.Run(() => NormalizePathRefs(input)).ConfigureAwait(true);
        await _pages.Agent.PostToPageAsync(new { type = "agentPathRefs", reqId, ok = true, refs });
    }

    private static List<object> NormalizePathRefs(IReadOnlyList<string> paths)
    {
        var list = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths)
        {
            try
            {
                var full = Path.GetFullPath(raw.Trim().Trim('"'));
                if (!seen.Add(full))
                    continue;

                string kind;
                if (Directory.Exists(full))
                    kind = "folder";
                else if (File.Exists(full))
                    kind = "file";
                else
                    continue;

                var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                    name = full;

                list.Add(new { path = full, name, kind });
            }
            catch
            {
                // skip invalid paths
            }
        }

        return list;
    }

    private async Task HandleAgentPathPickerAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var query = msg.TryGetProperty("query", out var qEl) ? qEl.GetString() ?? "" : "";
        var limit = 48;
        if (msg.TryGetProperty("limit", out var limEl) && limEl.TryGetInt32(out var lim))
            limit = Math.Clamp(lim, 8, 80);

        var workspace = AgentWorkspace.ResolveRoot(_config);
        var entries = await Task.Run(() => CollectPathPickerEntries(workspace, query, limit)).ConfigureAwait(true);
        await _pages.Agent.PostToPageAsync(new { type = "agentPathPicker", reqId, ok = true, entries });
    }

    private async Task HandleAgentPickFolderAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var picked = await PickWorkspaceFolderOnUiAsync("选择要引用的文件夹").ConfigureAwait(true);
        object? entry = null;
        if (!string.IsNullOrWhiteSpace(picked))
        {
            var full = Path.GetFullPath(picked);
            var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
                name = AgentWorkspaceRecents.DisplayName(full);
            entry = new { path = full, name, kind = "folder" };
        }

        await _pages.Agent.PostToPageAsync(new { type = "agentPickFolder", reqId, ok = entry is not null, entry });
    }

    private async Task HandleAgentPickReferencesAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var kind = msg.TryGetProperty("kind", out var kEl) ? (kEl.GetString() ?? "file").Trim() : "file";

        var paths = new List<string>();
        if (string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase))
        {
            var picked = await PickWorkspaceFolderOnUiAsync("选择要引用的文件夹").ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(picked))
                paths.Add(picked);
        }
        else
        {
            var picked = await PickReferenceFilesOnUiAsync().ConfigureAwait(true);
            if (picked?.Count > 0)
                paths.AddRange(picked);
        }

        var refs = paths.Count == 0
            ? new List<object>()
            : await Task.Run(() => NormalizePathRefs(paths)).ConfigureAwait(true);
        await _pages.Agent.PostToPageAsync(new { type = "agentPathRefs", reqId, ok = refs.Count > 0, refs });
    }

    private static async Task<List<string>> PickReferenceFilesOnUiAsync()
    {
        var picked = new List<string>();
        await RunOnUiAsync(() =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要引用的文件",
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var name in dlg.FileNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        picked.Add(name);
                }
            }

            return Task.CompletedTask;
        });
        return picked;
    }

    private static List<object> CollectPathPickerEntries(string workspace, string? query, int maxCount)
    {
        var results = new List<(int Rank, object Entry)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var q = (query ?? "").Trim();

        void TryAdd(string path, string section, int rank)
        {
            if (results.Count >= maxCount)
                return;
            try
            {
                var full = Path.GetFullPath(path);
                if (!seen.Add(full))
                    return;

                string kind;
                if (Directory.Exists(full))
                    kind = "folder";
                else if (File.Exists(full))
                    kind = "file";
                else
                    return;

                var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                    name = AgentWorkspaceRecents.DisplayName(full);

                if (!string.IsNullOrWhiteSpace(q)
                    && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                    && full.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    return;

                results.Add((rank, new { path = full, name, kind, section }));
            }
            catch
            {
                // skip invalid
            }
        }

        if (Directory.Exists(workspace))
            TryAdd(workspace, "工作区", 0);

        foreach (var recent in AgentWorkspaceRecents.List())
            TryAdd(recent, "最近", 1);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            TryAdd(home, "最近", 2);

        if (Directory.Exists(workspace))
            ScanWorkspaceForPicker(workspace, workspace, q, TryAdd, results, maxCount);

        if (!string.IsNullOrWhiteSpace(q) && (q.Contains(':') || q.StartsWith("\\\\", StringComparison.Ordinal)))
            BrowseAbsolutePathHint(q, TryAdd, results, maxCount);

        return results
            .OrderBy(r => r.Rank)
            .Take(maxCount)
            .Select(r => r.Entry)
            .ToList();
    }

    private static void ScanWorkspaceForPicker(
        string workspaceRoot,
        string dir,
        string q,
        Action<string, string, int> tryAdd,
        List<(int Rank, object Entry)> results,
        int maxCount)
    {
        if (results.Count >= maxCount)
            return;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch
        {
            return;
        }

        var folders = new List<string>();
        var files = new List<string>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name))
                continue;
            if (Directory.Exists(entry))
            {
                if (ShouldSkipWorkspaceDir(name))
                    continue;
                folders.Add(entry);
            }
            else
            {
                var rel = Path.GetRelativePath(workspaceRoot, entry).Replace('\\', '/');
                if (ShouldSkipWorkspaceFile(rel))
                    continue;
                files.Add(entry);
            }
        }

        folders.Sort(StringComparer.OrdinalIgnoreCase);
        files.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            tryAdd(folder, "文件夹", 10);
            if (results.Count >= maxCount)
                return;
            ScanWorkspaceForPicker(workspaceRoot, folder, q, tryAdd, results, maxCount);
            if (results.Count >= maxCount)
                return;
        }

        foreach (var file in files)
        {
            tryAdd(file, "文件", 20);
            if (results.Count >= maxCount)
                return;
        }
    }

    private static void BrowseAbsolutePathHint(
        string q,
        Action<string, string, int> tryAdd,
        List<(int Rank, object Entry)> results,
        int maxCount)
    {
        if (results.Count >= maxCount)
            return;

        string? dir = null;
        try
        {
            if (Directory.Exists(q))
            {
                dir = q;
            }
            else
            {
                var parent = Path.GetDirectoryName(q.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    dir = parent;
            }
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dir))
            return;

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(dir);
        }
        catch
        {
            return;
        }

        foreach (var entry in entries.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(entry);
            if (string.IsNullOrEmpty(name))
                continue;
            if (Directory.Exists(entry) && ShouldSkipWorkspaceDir(name))
                continue;
            if (!string.IsNullOrWhiteSpace(q)
                && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                && entry.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            tryAdd(entry, Directory.Exists(entry) ? "文件夹" : "文件", 5);
            if (results.Count >= maxCount)
                return;
        }
    }

    private static List<string> CollectWorkspaceFiles(string workspace, string? query, int maxCount)
    {
        var files = new List<string>(Math.Min(maxCount, 32));
        if (!Directory.Exists(workspace) || maxCount <= 0)
            return files;

        var q = query?.Trim();
        var queue = new Queue<string>();
        queue.Enqueue(workspace);

        while (queue.Count > 0 && files.Count < maxCount)
        {
            string dir;
            try
            {
                dir = queue.Dequeue();
            }
            catch
            {
                break;
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name)) continue;

                if (Directory.Exists(entry))
                {
                    if (ShouldSkipWorkspaceDir(name)) continue;
                    queue.Enqueue(entry);
                    continue;
                }

                var rel = Path.GetRelativePath(workspace, entry).Replace('\\', '/');
                if (ShouldSkipWorkspaceFile(rel)) continue;
                if (!string.IsNullOrWhiteSpace(q) &&
                    !rel.Contains(q, StringComparison.OrdinalIgnoreCase))
                    continue;

                files.Add(rel);
                if (files.Count >= maxCount)
                    break;
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    private static bool ShouldSkipWorkspaceDir(string name) =>
        name is ".git" or "node_modules" or "bin" or "obj" or "dist" or "build" or ".venv" or "__pycache__"
        or "publish" or ".vs" or ".idea";

    private static bool ShouldSkipWorkspaceFile(string rel)
    {
        var parts = rel.Split('/', '\\');
        foreach (var p in parts)
        {
            if (ShouldSkipWorkspaceDir(p))
                return true;
        }

        return rel.StartsWith(".git/", StringComparison.Ordinal);
    }

    private async Task HandleAgentWorkspaceReadSnippetAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var rel = msg.TryGetProperty("path", out var pEl) ? pEl.GetString() ?? "" : "";
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var paths = new SandboxPathResolver(workspace);
            using var doc = JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new { file_path = rel, limit = 80 }));
            var snippet = HarnessReadFileTool.Execute(doc.RootElement, paths).Output;
            await _pages.Agent.PostToPageAsync(new { type = "agentWorkspaceReadSnippet", reqId, ok = true, snippet });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentWorkspaceReadSnippet",
                reqId,
                ok = false,
                error = ex.Message
            });
        }
    }

    private async Task HandleAgentPlanGetAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        ReloadConfig();
        await _pages.Agent.PostToPageAsync(new
        {
            type = "agentPlanGet",
            reqId,
            ok = true,
            plan = _config.AgentSessionPlanMarkdown ?? ""
        });
    }

    private async Task HandleAgentHarnessReloadAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            HarnessRegistryReload.ReloadAll();
            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentHarnessReload",
                reqId,
                ok = true,
                reloadedAtUtc = HarnessRegistryReload.LastReloadUtc.ToString("O")
            });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentHarnessReload", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentSkillsAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var extraRoots = _config.AgentSkillExtraRoots;
            var skills = await Task.Run(() =>
                HarnessSkillRegistry.List(workspace, extraRoots)
                    .Select(s => new { s.Id, s.Name, s.Description, s.Source, s.FilePath })
                    .ToList()).ConfigureAwait(true);
            await _pages.Agent.PostToPageAsync(new { type = "agentSkills", reqId, ok = true, skills });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentSkills", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentRunsListAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var limit = msg.TryGetProperty("limit", out var limEl) && limEl.TryGetInt32(out var lim)
                ? Math.Clamp(lim, 1, 200)
                : 50;
            var runs = HarnessRunTraceStore.ListRuns(workspace, limit)
                .Select(m => new
                {
                    m.RunId,
                    m.TraceId,
                    langfuseUrl = HarnessLangfuseExporter.IsConfigured(_config) && !string.IsNullOrWhiteSpace(m.TraceId)
                        ? HarnessLangfuseExporter.BuildTraceUrl(_config, m.TraceId)
                        : null,
                    m.StartedUtc,
                    m.EndedUtc,
                    m.DurationMs,
                    m.PromptPreview,
                    m.AnswerPreview,
                    m.PromptTokens,
                    m.CompletionTokens,
                    m.TotalTokens,
                    m.ToolCallCount,
                    m.Strategy,
                    m.Phase
                })
                .ToList();
            await _pages.Agent.PostToPageAsync(new { type = "agentRuns", reqId, ok = true, runs });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentRuns", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentRunLoadAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var runId = msg.TryGetProperty("runId", out var idEl) ? idEl.GetString() : null;
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var maxSpans = msg.TryGetProperty("maxSpans", out var msEl) && msEl.TryGetInt32(out var ms)
                ? Math.Clamp(ms, 1, 500)
                : 200;
            var loaded = HarnessRunTraceStore.LoadRun(workspace, runId ?? "", maxSpans);
            if (loaded is null)
            {
                await _pages.Agent.PostToPageAsync(new { type = "agentRunTrace", reqId, ok = false, error = "run not found" });
                return;
            }

            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentRunTrace",
                reqId,
                ok = true,
                runId = loaded.RunId,
                meta = loaded.Meta,
                spans = loaded.Spans.Select(s => new
                {
                    s.SpanId,
                    s.ParentSpanId,
                    s.Name,
                    s.StartUtc,
                    s.DurationMs,
                    s.Status,
                    s.Attributes
                })
            });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentRunTrace", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentMemorySearchAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var query = msg.TryGetProperty("query", out var qEl) ? qEl.GetString() ?? "" : "";
        try
        {
            ReloadConfig();
            using var store = new HarnessSemanticMemoryStore();
            var limit = msg.TryGetProperty("limit", out var limEl) && limEl.TryGetInt32(out var lim)
                ? Math.Clamp(lim, 1, 100)
                : 20;
            var hits = store.SearchByTextPrefix(query, limit)
                .Select(h => new
                {
                    h.Id,
                    h.Scope,
                    h.Text,
                    h.MetadataJson,
                    updatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(h.UpdatedAtUnix).ToString("O")
                })
                .ToList();
            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentMemory",
                reqId,
                ok = true,
                query,
                count = store.Count(),
                hits
            });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentMemory", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentMemoryClearSessionAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            using var store = new HarnessSemanticMemoryStore();
            var deleted = store.ClearScopePrefix("session:");
            await _pages.Agent.PostToPageAsync(new { type = "agentMemoryClearSession", reqId, ok = true, deleted });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentMemoryClearSession", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentMemoryForgetAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        try
        {
            using var store = new HarnessSemanticMemoryStore();
            var ok = !string.IsNullOrWhiteSpace(id) && store.Forget(id!);
            await _pages.Agent.PostToPageAsync(new { type = "agentMemoryForget", reqId, ok, id });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentMemoryForget", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentGraphListAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var graphs = HarnessGraphRegistry.List(workspace)
                .Select(g => new { g.Id, g.NodeCount, g.EdgeCount, g.Source, g.FilePath })
                .ToList();
            await _pages.Agent.PostToPageAsync(new { type = "agentGraphs", reqId, ok = true, graphs });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentGraphs", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentResumeThreadAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var threadId = msg.TryGetProperty("threadId", out var tEl) ? tEl.GetString() : null;
        var task = msg.TryGetProperty("task", out var taskEl) ? taskEl.GetString() ?? "" : "";
        try
        {
            ReloadConfig();
            var cp = HarnessGraphCheckpoint.Load(threadId ?? "");
            if (cp is null || string.IsNullOrWhiteSpace(cp.GraphId))
            {
                await _pages.Agent.PostToPageAsync(new { type = "agentResumeThread", reqId, ok = false, error = "thread not found" });
                return;
            }

            var workspace = AgentWorkspace.ResolveRoot(_config);
            var runner = GetHarnessRunner();
            var result = await runner.RunAsync(
                new HarnessRunRequest
                {
                    Config = _config,
                    Prompt = string.IsNullOrWhiteSpace(task) ? "Resume graph thread" : task,
                    Strategy = AgentStrategies.GraphStrategy(cp.GraphId),
                    AgentSessionId = threadId,
                    ResumeGraphThreadId = threadId
                },
                new HarnessRunCallbacks { OnLog = line => _ = _pages.PostToPageAsync(new { type = "agentLog", text = line }) },
                CancellationToken.None);

            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentResumeThread",
                reqId,
                ok = true,
                threadId,
                answer = result.Answer,
                runId = result.RunId
            });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentResumeThread", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentSkillsReindexAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        ReloadConfig();
        var workspace = AgentWorkspace.ResolveRoot(_config);
        var extra = _config.AgentSkillExtraRoots;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await HarnessSkillCatalogIndexer.ReindexAsync(
                    workspace,
                    extra,
                    p => _ = _pages.Agent.PostToPageAsync(new
                    {
                        type = "agentSkillReindexProgress",
                        reqId,
                        scanned = p.Scanned,
                        indexed = p.Indexed,
                        skipped = p.Skipped,
                        done = p.Done
                    }),
                    CancellationToken.None);
                HarnessSkillRegistry.InvalidateCache();
                await _pages.Agent.PostToPageAsync(new
                {
                    type = "agentSkillsReindex",
                    reqId,
                    ok = true,
                    count = result.Count,
                    skipped = result.Skipped
                });
            }
            catch (Exception ex)
            {
                await _pages.Agent.PostToPageAsync(new { type = "agentSkillsReindex", reqId, ok = false, error = ex.Message });
            }
        });
        await _pages.Agent.PostToPageAsync(new { type = "agentSkillsReindex", reqId, ok = true, started = true });
    }

    private async Task HandleAgentSkillsSearchAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var query = msg.TryGetProperty("query", out var qEl) ? qEl.GetString() ?? "" : "";
        try
        {
            var limit = msg.TryGetProperty("limit", out var limEl) && limEl.TryGetInt32(out var lim)
                ? Math.Clamp(lim, 1, 100)
                : 20;
            var hits = HarnessSkillCatalogIndexer.Search(query, limit)
                .Select(h => new { h.Id, h.Name, h.Description, h.Source, h.FilePath, h.Tags })
                .ToList();
            await _pages.Agent.PostToPageAsync(new { type = "agentSkillsSearch", reqId, ok = true, query, hits });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentSkillsSearch", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentBlocksListAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var blocks = HarnessBlockRegistry.List(workspace)
                .Select(b => new { b.Id, b.Type, b.Description, b.Tool, b.Command, b.SkillId, b.GraphId })
                .ToList();
            await _pages.Agent.PostToPageAsync(new { type = "agentBlocks", reqId, ok = true, blocks });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentBlocks", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentCheckpointAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            AgentDesktopConfigSync.Apply(_config);
            var cp = HarnessCheckpointStore.Load();
            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentCheckpoint",
                reqId,
                ok = true,
                checkpoint = cp
            });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentCheckpoint", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentPlaybooksAsync(JsonElement msg)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            AgentDesktopConfigSync.Apply(_config);
            var workspace = AgentWorkspace.ResolveRoot(_config);
            var playbooks = await Task.Run(() =>
                HarnessPlaybookRegistry.List(workspace)
                    .Select(p => new
                    {
                        p.Id,
                        p.Name,
                        p.Description,
                        p.Strategy,
                        p.HasVerify,
                        p.VerifyStepCount,
                        p.Source
                    })
                    .ToList()).ConfigureAwait(true);
            await _pages.Agent.PostToPageAsync(new { type = "agentPlaybooks", reqId, ok = true, playbooks });
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentPlaybooks", reqId, ok = false, error = ex.Message });
        }
    }

    private async Task HandleAgentSessionAsync(JsonElement msg, string type)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            switch (type)
            {
                case "agentSessionList":
                    if (_config.AgentSessionAutoCleanup)
                    {
                        _ = _agentSessions.ApplyRetention(
                            _config.AgentSessionRetentionDays,
                            _config.AgentSessionMaxStorageGb);
                    }

                    await ReplyAgentSessionAsync(reqId, new
                    {
                        ok = true,
                        metas = _agentSessions.ListMetas()
                    });
                    break;
                case "agentSessionLoad":
                {
                    var id = msg.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var session = string.IsNullOrWhiteSpace(id) ? null : _agentSessions.Load(id);
                    await ReplyAgentSessionAsync(reqId, new { ok = session is not null, session });
                    break;
                }
                case "agentSessionSave":
                {
                    if (!msg.TryGetProperty("session", out var sessEl))
                    {
                        await ReplyAgentSessionAsync(reqId, new { ok = false, error = "session 缺失" });
                        return;
                    }

                    var session = JsonSerializer.Deserialize<AgentSessionData>(
                        sessEl.GetRawText(),
                        AgentSessionJson.Options);
                    if (session is null || string.IsNullOrWhiteSpace(session.Id))
                    {
                        await ReplyAgentSessionAsync(reqId, new { ok = false, error = "session 无效" });
                        return;
                    }

                    _agentSessions.Save(session);
                    await ReplyAgentSessionAsync(reqId, new { ok = true, id = session.Id });
                    break;
                }
                case "agentSessionDelete":
                {
                    var id = msg.TryGetProperty("id", out var delEl) ? delEl.GetString() : null;
                    var ok = !string.IsNullOrWhiteSpace(id) && _agentSessions.Delete(id!);
                    await ReplyAgentSessionAsync(reqId, new { ok });
                    break;
                }
                case "agentSessionRename":
                {
                    var id = msg.TryGetProperty("id", out var rnIdEl) ? rnIdEl.GetString() : null;
                    var title = msg.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
                    var ok = !string.IsNullOrWhiteSpace(id) && _agentSessions.Rename(id!, title ?? "");
                    await ReplyAgentSessionAsync(reqId, new { ok });
                    break;
                }
                case "agentSessionPin":
                {
                    var id = msg.TryGetProperty("id", out var pinIdEl) ? pinIdEl.GetString() : null;
                    var pinned = msg.TryGetProperty("pinned", out var pinEl) && pinEl.GetBoolean();
                    var ok = !string.IsNullOrWhiteSpace(id) && _agentSessions.SetPinned(id!, pinned);
                    await ReplyAgentSessionAsync(reqId, new { ok, pinned });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            await ReplyAgentSessionAsync(reqId, new { ok = false, error = ex.Message });
        }
    }

    private Task ReplyAgentSessionAsync(string? reqId, object payload) =>
        _pages.Agent.PostToPageAsync(new { type = "agentSession", reqId, payload });

    /// <summary>将 API 管理账户 Token 注入网页桥接（不从普通对话页读取）。</summary>
    public async Task SyncApiBridgeFromApiAccountsAsync()
    {
        ReloadConfig();
        var token = AccountCredentials.ResolveWebUserTokenForRoute(null, _config, "deepseek");
        if (string.IsNullOrWhiteSpace(token))
            return;
        await _pages.SyncApiBridgeTokenAsync(token);
    }

    public async Task OnChatNavigationCompletedAsync() =>
        await RefreshLoginStateAsync();

    public Task OnAgentNavigationCompletedAsync()
    {
        _ = PushCachedLoginStateAsync();
        _ = PushLoginStateAfterAgentNavAsync();
        _ = RefreshLoginStateBackgroundAsync();
        return Task.CompletedTask;
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
            await SyncApiBridgeFromApiAccountsAsync();
            ReloadConfig();
            await PushLoginStateAsync();
            await NotifyApiInfoAsync();
        }
        catch
        {
            // 后台同步失败不影响已展示的登录状态
        }
    }

    public async Task PushLoginStateToPageAsync() => await PushLoginStateAsync();

    private async Task PushLoginStateAsync()
    {
        ReloadConfig();
        var loggedIn = HasConfiguredDeepSeekApiAccount(_config);
        await _pages.PushAgentAuthHintAsync(loggedIn);
        await _pages.PostToPageAsync(new { type = "loginState", loggedIn });
        await PushWorkspaceStateAsync();
    }

    private void QueueAgentSurfaceFollowUp(JsonElement msg)
    {
        ConfigStore.Save(_config);
        _ = SyncApiBridgeFromApiAccountsAsync();
        _ = PushCachedLoginStateAsync();
        _ = PushLoginStateAfterAgentNavAsync();
        _ = RefreshLoginStateBackgroundAsync();
    }

    private void QueueChatSurfaceFollowUp(JsonElement msg, bool skipBurst = false)
    {
        ConfigStore.Save(_config);
        _ = RefreshLoginStateBackgroundAsync();
        if (!skipBurst)
            _pages.RequestChatInject("chat_surface_followup", forceReset: false);
    }

    private async Task PushCachedLoginStateAsync()
    {
        try
        {
            ReloadConfig();
            var loggedIn = HasConfiguredDeepSeekApiAccount(_config);
            await _pages.PushAgentAuthHintAsync(loggedIn);
            await _pages.PostToPageAsync(new { type = "loginState", loggedIn });
        }
        catch
        {
            // 页面尚未就绪时由后续重试补齐
        }
    }

    private static bool TryGetMessageProperty(JsonElement msg, string name, out JsonElement value)
    {
        value = default;
        if (msg.ValueKind != JsonValueKind.Object)
            return false;
        return msg.TryGetProperty(name, out value);
    }

    private DsdApiIpcBridge GetDsdApiIpc() =>
        _dsdApiIpc ??= new DsdApiIpcBridge(
            _localApi,
            ResolveDsdApiWebInject(),
            () => _owner,
            SyncDsdApiStackAsync,
            EmitDsdApiIpcEvent);

    private void EmitDsdApiIpcEvent(string channel, object?[] args) =>
        _ = DispatchDsdApiIpcEventAsync(channel, args);

    private async Task DispatchDsdApiIpcEventAsync(string channel, object?[] args)
    {
        var payload = new { type = "ipcEvent", channel, args };
        try
        {
            if (AppNavigation.IsEmbeddedApiManagementPage(_pages.AgentSource))
                await _pages.Agent.PostWebMessageAsync(payload).ConfigureAwait(false);
            await _pages.Agent.PostToPageAsync(payload).ConfigureAwait(false);
        }
        catch
        {
            // page may not be ready
        }
    }

    private WebInjectService ResolveDsdApiWebInject() => (WebInjectService)_pages.Agent;

    private async Task SyncDsdApiStackAsync(AppConfig config, CancellationToken ct)
    {
        AgentDesktopConfigSync.Apply(config);
        await EmbeddedStackCoordinator.EnsureLinkedAsync(config, _localApi, ChatWebInject, ct)
            .ConfigureAwait(false);
        DsdApiHealth? health = null;
        try
        {
            var bridgeToken = AccountCredentials.ResolveWebUserTokenForRoute(null, config, "deepseek");
            health = await _pages.ProbeDsdApiHealthAsync(bridgeToken, InternalChatChannel.DesktopV1, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        DsdApiProviderService.WriteIntegrationFile(config, health);
    }

    private EmbeddedSettingsSupport GetEmbeddedSettings() =>
        _embeddedSettings ??= new EmbeddedSettingsSupport(
            _mcpHub,
            () => _owner,
            () => _config,
            cfg => _config = cfg,
            () => _pages.Agent);

    private async Task EnsureAgentAndShowEmbeddedPanelAsync(string panel)
    {
        if (!_pages.IsAgentVisible)
        {
            await _pages.WorkMode.ShowAgentSurfaceAsync();
            await _pages.WorkMode.BroadcastImmediateAsync();
        }
        await ShowEmbeddedPanelAsync(panel);
    }

    private async Task ShowEmbeddedPanelAsync(string panel)
    {
        ReloadConfig();
        GetDsdApiIpc().RefreshConfig(_config);
        if (_embeddedPanels.TryGetPanel(panel, out var descriptor))
            _embedCoordinator.NotifyPanelVisible(descriptor.Id);
        await _pages.Agent.PostToPageAsync(new { type = "showEmbeddedPanel", panel });
    }

    private async Task HandleEmbeddedIpcInvokeAsync(JsonElement msg)
    {
        var id = msg.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var parsed) ? parsed : 0;
        var channel = msg.TryGetProperty("channel", out var chEl) ? chEl.GetString() ?? "" : "";
        var args = msg.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array
            ? argsEl.EnumerateArray().Select(x => x.Clone()).ToArray()
            : Array.Empty<JsonElement>();

        object? result = null;
        string? error = null;
        try
        {
            result = await GetDsdApiIpc().InvokeAsync(channel, args);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (AppNavigation.IsEmbeddedApiManagementPage(_pages.AgentSource))
            await _pages.Agent.PostWebMessageAsync(new { type = "ipcResult", id, result, error });
        else
            await _pages.Agent.PostToPageAsync(new { type = "ipcResult", id, result, error });
    }

    private void OpenAgentConfigFile()
    {
        AgentDesktopConfigSync.Apply(_config);
        var path = AgentDesktopConfigSync.ConfigPath;
        Directory.CreateDirectory(AgentDesktopConfigSync.HomeDirectory);
        if (!File.Exists(path))
            File.WriteAllText(path, "# Generated by DeepSeek Desktop\n", Encoding.UTF8);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private void OpenSettingsWindow()
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
        _agentWindow.StopRequested += (_, _) => CancelAgentRun();
        _agentWindow.Closed += (_, _) => _agentWindow = null;
        _agentWindow.Show();
    }

    private async Task RunAgentAsync(
        string task, string mode, bool deepThink, bool smartSearch, bool mcpOn, string strategy,
        IReadOnlyList<string>? refFileIds = null,
        string? sessionId = null,
        string? harnessState = null,
        string? playbookId = null,
        string? skillId = null,
        string? webChatSessionIdHint = null)
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
        await SyncApiBridgeFromApiAccountsAsync();
        AgentModeHelper.ApplyAgentDefaults(_config);
        var historyCount = 0;
        string? webChatSessionIdFromSession = webChatSessionIdHint;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                var session = _agentSessions.Load(sessionId);
                historyCount = session?.Messages?.Count ?? 0;
                webChatSessionIdFromSession ??= session?.WebChatSessionId;
            }
            catch
            {
                // ignore
            }
        }

        var autoSel = AgentModeHelper.ApplyChatMode(
            _config,
            mode,
            deepThink,
            smartSearch,
            task,
            strategy,
            refFileIds?.Count ?? 0,
            historyCount);
        AgentModeHelper.ApplyExecuteTaskProfile(_config, mode, strategy, task);
        _pages.AgentRefFileIds = refFileIds ?? Array.Empty<string>();
        ConfigStore.Save(_config);
        _localApi.UpdateConfig(_config);

        using var featureScope = DsdAgentApiScope.Begin(deepThink, smartSearch);
        _localApi.EnsureAgentScopedListening();
        AgentDesktopConfigSync.Apply(_config);
        using var debugLog = _config.AgentDebugLogEnabled
            ? AgentDebugLogger.Begin(task, _config.AgentDebugLogConsole)
            : null;

        debugLog?.Write("CONFIG", $"深度思考={deepThink} 智能搜索={smartSearch} strategy={strategy} mcp={mcpOn}");
        if (autoSel is not null)
            debugLog?.Write("CONFIG", $"[Auto] {autoSel.ReasonZh} → {autoSel.ModelId} ({autoSel.Tier})");
        debugLog?.Write("CONFIG", $"Harness=native DSD API port={_config.LocalApiPort}");

        void Log(string line)
        {
            debugLog?.Write("AGENT", line);
            Application.Current.Dispatcher.Invoke(() => _agentWindow?.AppendLog(line));
            OnMessage?.Invoke(this, new AgentMessageEventArgs("log", line, "log"));
            if (PostToWebUi)
                _ = _pages.PostToPageAsync(new { type = "agentLog", text = line });
            if (line.StartsWith("Final Answer: ", StringComparison.OrdinalIgnoreCase))
            {
                var ans = line["Final Answer: ".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(ans))
                {
                    OnMessage?.Invoke(this, new AgentMessageEventArgs("assistant", ans));
                    if (PostToWebUi)
                        _ = _pages.PostToPageAsync(new { type = "agentAnswer", text = ans });
                }
            }
        }

        try
        {
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Running));
            if (PostToWebUi)
                await _pages.PostToPageAsync(new { type = "agentStarted", task });

            if (autoSel is not null)
            {
                Log($"[Auto] {autoSel.ProviderName} / {autoSel.ModelId} — {autoSel.ReasonZh}");
                if (PostToWebUi)
                {
                    await _pages.PostToPageAsync(new
                    {
                        type = "agentModelResolved",
                        providerId = autoSel.ProviderId,
                        providerName = autoSel.ProviderName,
                        model = autoSel.ModelId,
                        tier = autoSel.Tier,
                        reason = autoSel.ReasonZh,
                        auto = true
                    });
                }
            }
            else if (PostToWebUi)
            {
                await _pages.PostToPageAsync(new
                {
                    type = "agentModelResolved",
                    providerId = _config.AgentDefaultProviderId,
                    model = _config.Model,
                    auto = false
                });
            }

            if (!HasConfiguredDeepSeekApiAccount(_config)
                && !AgentChatClientFactory.UsesDirectApi(_config))
            {
                Log("请在 API 管理中添加 DeepSeek 账户并填写用户 Token。");
                return;
            }

            if (debugLog is not null)
                Log("调试日志: " + debugLog.LogFilePath);

            if (deepThink || smartSearch)
            {
                var flags = new List<string>();
                if (deepThink) flags.Add("深度思考");
                if (smartSearch) flags.Add("智能搜索");
                Log("特性: " + string.Join(" · ", flags) + "（API 管理）");
            }

            void PostActivity(DeepSeekBrowser.Services.AgentUiActivity a)
            {
                debugLog?.Write("ACTIVITY", $"{a.Verb} {a.Target}" + (a.Detail is not null ? $" ({a.Detail})" : ""));
                _ = _pages.PostToPageAsync(new
                {
                    type = "agentActivity",
                    verb = a.Verb,
                    target = a.Target,
                    detail = a.Detail
                });
            }

            void PostFilePreview(DeepSeekBrowser.Services.Harness.AgentFilePreview p)
            {
                if (!PostToWebUi) return;
                debugLog?.Write("FILE_PREVIEW", p.Path + " (" + p.Content.Length + " chars)");
                _ = _pages.PostToPageAsync(new
                {
                    type = "agentFilePreview",
                    path = p.Path,
                    lang = p.Language,
                    content = p.Content,
                    complete = p.Complete
                });
            }

            void PostPendingPatch(DeepSeekBrowser.Services.Harness.AgentPendingPatch p)
            {
                if (!PostToWebUi) return;
                debugLog?.Write("PATCH_PENDING", p.Path + " id=" + p.PatchId);
                _ = _pages.PostToPageAsync(new
                {
                    type = "agentPendingPatch",
                    patchId = p.PatchId,
                    path = p.Path,
                    lang = p.Language,
                    originalContent = p.OriginalContent,
                    patchedContent = p.PatchedContent,
                    unifiedDiff = p.UnifiedDiff
                });
            }

            void PostThinking(string text, bool appendMode)
            {
                debugLog?.LogThinkingDelta(text);
                OnStreamDelta?.Invoke(this, new AgentStreamDeltaEventArgs(text, appendMode, isThinking: true));
                if (PostToWebUi)
                    _ = _pages.PostToPageAsync(new { type = "agentThinking", text, append = appendMode });
            }

            var postToAgentUi = PostToWebUi;

            void PostAnswerDelta(string delta, bool appendAnswer)
            {
                if (string.IsNullOrEmpty(delta)) return;
                OnStreamDelta?.Invoke(this, new AgentStreamDeltaEventArgs(delta, appendAnswer, false));
                if (postToAgentUi)
                    _ = _pages.PostToPageAsync(new { type = "agentAnswer", text = delta, append = appendAnswer });
            }

            try
            {
                using var bridgeWarm = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bridgeWarm.CancelAfter(TimeSpan.FromSeconds(3));
                await _pages.EnsureApiBridgeReadyAsync(bridgeWarm.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Log("API 管理桥接预热超时，继续尝试…");
            }
            catch (Exception ex)
            {
                Log("API 管理桥接: " + ex.Message);
            }

            if (mcpOn && _mcpHub.ConnectedCount == 0)
            {
                Log("正在连接已启用的 MCP 服务器…");
                var mcpErrors = await ConnectEnabledMcpServersAsync();
                foreach (var err in mcpErrors)
                    Log("MCP: " + err);
            }

            string answer;
            string? persistedState;
            string? webChatSessionId;

            var harnessResult = await GetHarnessRunner().RunAsync(
                new HarnessRunRequest
                {
                    Config = _config,
                    Prompt = task,
                    Strategy = strategy ?? _config.DefaultAgentStrategy ?? AgentStrategies.Execute,
                    ExistingHarnessState = harnessState,
                    WebChatSessionId = webChatSessionIdFromSession,
                    RefFileIds = refFileIds ?? Array.Empty<string>(),
                    PlaybookId = playbookId,
                    SkillId = skillId,
                    AgentSessionId = sessionId
                },
                new HarnessRunCallbacks
                {
                    OnLog = Log,
                    OnThinking = PostThinking,
                    OnAnswerDelta = PostAnswerDelta,
                    OnActivity = PostActivity,
                    OnFilePreview = PostFilePreview,
                    OnPendingPatch = PostPendingPatch,
                    OnShellOutput = chunk =>
                    {
                        if (PostToWebUi)
                            _ = _pages.PostToPageAsync(new { type = "agentShellOutput", text = chunk });
                    },
                    OnPhaseChanged = phase =>
                    {
                        if (!PostToWebUi) return;
                        _ = _pages.PostToPageAsync(new
                        {
                            type = "agentPhase",
                            phase = HarnessPhasePolicy.TraceLabel(phase),
                            sessionId
                        });
                    },
                    OnPlanUpdated = plan =>
                    {
                        if (!PostToWebUi) return;
                        _ = _pages.PostToPageAsync(new
                        {
                            type = "agentPlanGet",
                            plan,
                            sessionId
                        });
                    }
                },
                ct);
            answer = harnessResult.Answer;
            persistedState = harnessResult.HarnessState;
            webChatSessionId = harnessResult.WebChatSessionId;
            RememberWebChatSession(webChatSessionId);
            _ = _pages.SyncChatSessionAsync(webChatSessionId);

            if (!string.IsNullOrWhiteSpace(sessionId)
                && !string.IsNullOrWhiteSpace(harnessResult.LastCheckpointHash))
            {
                ApplyCheckpointToSession(sessionId, harnessResult.LastCheckpointHash);
                if (PostToWebUi)
                {
                    await _pages.PostToPageAsync(new
                    {
                        type = "agentCheckpoint",
                        sessionId,
                        checkpointHash = harnessResult.LastCheckpointHash
                    });
                }
            }

            var summary = answer;

            await _pages.PostToPageAsync(new
            {
                type = "agentHarnessState",
                sessionId,
                harnessState = persistedState,
                webChatSessionId,
                phase = TryReadHarnessPhase(persistedState)
            });
            await _pages.PostToPageAsync(new
            {
                type = "agentTuiThread",
                sessionId,
                tuiThreadId = persistedState,
                harnessState = persistedState
            });

            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Completed, summary, answer));
            if (PostToWebUi)
                await _pages.PostToPageAsync(new
                {
                    type = "agentDone",
                    summary,
                    answer,
                    runId = harnessResult.RunId,
                    metrics = new
                    {
                        durationMs = harnessResult.DurationMs,
                        promptTokens = harnessResult.PromptTokens,
                        completionTokens = harnessResult.CompletionTokens,
                        llmCalls = harnessResult.LlmCallCount,
                        toolCalls = harnessResult.ToolCallCount,
                        patchesAccepted = harnessResult.PatchesAccepted,
                        patchesRejected = harnessResult.PatchesRejected,
                        patchAcceptRate = harnessResult.PatchAcceptRate
                    }
                });
        }
        catch (OperationCanceledException)
        {
            Log("已停止。");
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Cancelled, "已停止"));
            if (PostToWebUi)
                await _pages.PostToPageAsync(new { type = "agentDone", summary = "已停止", answer = "" });
        }
        catch (Exception ex)
        {
            Log("错误: " + ex.Message);
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Failed, ex.Message));
            if (PostToWebUi)
                await _pages.PostToPageAsync(new { type = "agentDone", summary = "失败: " + ex.Message });
        }
        finally
        {
            _localApi.ReleaseAgentScopedListening();
            _pages.AgentRefFileIds = Array.Empty<string>();
            _agentWindow?.SetRunning(false);
        }
    }

    /// <summary>供 DEEPSEEK_DESKTOP_VERIFY_AGENT=1 自检 Agent HELLO 链路。</summary>
    public async Task VerifyAgentHelloAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<AgentRunStateEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, AgentRunStateEventArgs e)
        {
            if (e.State is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Cancelled)
                tcs.TrySetResult(e);
        }

        OnRunStateChanged += Handler;
        try
        {
            WorkModeTrace.Write("AgentSelfTest: starting hello run");
            await RunAgentAsync("hello", "专家", deepThink: false, smartSearch: false, mcpOn: false, strategy: AgentStrategies.Execute)
                .ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            var result = await tcs.Task.ConfigureAwait(false);
            if (result.State == AgentRunState.Completed &&
                !string.IsNullOrWhiteSpace(result.Answer) &&
                !LooksLikeAgentError(result.Answer) &&
                !LooksLikeForcedBlueprint(result.Answer))
            {
                WorkModeTrace.Write($"AgentSelfTest: PASS answer={TrimForLog(result.Answer)}");
                return;
            }

            var msg = result.State == AgentRunState.Completed
                ? "empty or error-like answer: " + (result.Answer ?? "")
                : result.Summary ?? result.State.ToString();
            WorkModeTrace.Write("AgentSelfTest: FAIL " + msg);
            throw new InvalidOperationException(msg);
        }
        finally
        {
            OnRunStateChanged -= Handler;
        }
    }

    /// <summary>供 DEEPSEEK_DESKTOP_VERIFY_AGENT_TASK=1 自检工具任务链路。</summary>
    public async Task VerifyAgentTaskAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<AgentRunStateEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? _, AgentRunStateEventArgs e)
        {
            if (e.State is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Cancelled)
                tcs.TrySetResult(e);
        }

        OnRunStateChanged += Handler;
        try
        {
            WorkModeTrace.Write("AgentTaskTest: starting list_dir task");
            await RunAgentAsync(
                    "请用 list_dir 工具列出工作区根目录下的条目，并用一句话总结。",
                    "专家",
                    deepThink: false,
                    smartSearch: false,
                    mcpOn: false,
                    strategy: AgentStrategies.Execute)
                .ConfigureAwait(false);
            using var reg = ct.Register(() => tcs.TrySetCanceled());
            var result = await tcs.Task.ConfigureAwait(false);
            if (result.State == AgentRunState.Completed &&
                !string.IsNullOrWhiteSpace(result.Answer) &&
                !LooksLikeAgentError(result.Answer))
            {
                WorkModeTrace.Write($"AgentTaskTest: PASS answer={TrimForLog(result.Answer)}");
                return;
            }

            var msg = result.State == AgentRunState.Completed
                ? "empty or error-like answer: " + (result.Answer ?? "")
                : result.Summary ?? result.State.ToString();
            WorkModeTrace.Write("AgentTaskTest: FAIL " + msg);
            throw new InvalidOperationException(msg);
        }
        finally
        {
            OnRunStateChanged -= Handler;
        }
    }

    private static bool LooksLikeForcedBlueprint(string text) =>
        text.Contains("## 目标", StringComparison.Ordinal) &&
        text.Contains("## 现状摘要", StringComparison.Ordinal);

    private static bool LooksLikeAgentError(string text) =>
        text.Contains("错误", StringComparison.Ordinal) ||
        text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("Win32", StringComparison.Ordinal) ||
        text.StartsWith("失败", StringComparison.Ordinal);

    private static string TrimForLog(string s) =>
        s.Length <= 120 ? s : s[..120] + "…";

    private async Task ConnectMcpAsync(Action<string> onLog, CancellationToken ct)
    {
        await _mcpHub.DisconnectAllAsync(ct);
        var errors = await _mcpHub.ConnectEnabledAsync(McpConfigInterop.MergeEnabledServers(_config), onLog, ct);
        onLog($"已连接 {_mcpHub.ConnectedCount} 个 MCP 服务");
        foreach (var err in errors)
            onLog("连接失败: " + err);
    }

    private async Task HandleAgentWorkspaceAsync(JsonElement msg, string type)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        try
        {
            ReloadConfig();
            switch (type)
            {
                case "agentWorkspaceGet":
                    await ReplyWorkspaceAsync(reqId, BuildWorkspacePayload());
                    break;
                case "agentWorkspaceSet":
                    if (msg.TryGetProperty("path", out var pathEl))
                    {
                        var path = pathEl.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            _config.AgentWorkspaceRoot = AgentWorkspaceRecents.Normalize(path);
                            AgentWorkspaceRecents.Touch(_config.AgentWorkspaceRoot);
                            ConfigStore.Save(_config);
                            AgentDesktopConfigSync.Apply(_config);
                        }
                    }
                    await ReplyWorkspaceAsync(reqId, BuildWorkspacePayload());
                    break;
                case "agentWorkspacePickFolder":
                    var picked = await PickWorkspaceFolderOnUiAsync();
                    if (!string.IsNullOrWhiteSpace(picked))
                    {
                        _config.AgentWorkspaceRoot = picked;
                        AgentWorkspaceRecents.Touch(picked);
                        ConfigStore.Save(_config);
                        AgentDesktopConfigSync.Apply(_config);
                    }
                    await ReplyWorkspaceAsync(reqId, BuildWorkspacePayload());
                    break;
                case "agentWorkspacePatch":
                    if (msg.TryGetProperty("defaultAgentStrategy", out var stratEl))
                        _config.DefaultAgentStrategy = HarnessStrategyResolver.Normalize(stratEl.GetString());
                    if (msg.TryGetProperty("agentModelAuto", out var autoEl))
                        _config.AgentModelAuto = autoEl.GetBoolean();
                    if (msg.TryGetProperty("agentManualModel", out var manualEl)
                        && manualEl.ValueKind == JsonValueKind.String)
                    {
                        var manual = manualEl.GetString();
                        if (!string.IsNullOrWhiteSpace(manual))
                            _config.AgentManualModel = manual.Trim();
                    }
                    if (msg.TryGetProperty("agentManualProviderId", out var manualProvEl)
                        && manualProvEl.ValueKind == JsonValueKind.String)
                    {
                        var mp = manualProvEl.GetString();
                        if (!string.IsNullOrWhiteSpace(mp))
                            _config.AgentManualProviderId = mp.Trim();
                    }
                    if (msg.TryGetProperty("agentAutoPreferProviderOrder", out var preferEl))
                        _config.AgentAutoPreferProviderOrder = preferEl.GetBoolean();
                    if (msg.TryGetProperty("agentAutoProviderOrder", out var orderEl)
                        && orderEl.ValueKind == JsonValueKind.Array)
                    {
                        _config.AgentAutoProviderOrder = orderEl.EnumerateArray()
                            .Select(e => e.GetString())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Select(s => s!.Trim())
                            .ToList();
                    }
                    ConfigStore.Save(_config);
                    AgentDesktopConfigSync.Apply(_config);
                    await ReplyWorkspaceAsync(reqId, BuildWorkspacePayload());
                    break;
            }
        }
        catch (Exception ex)
        {
            await _pages.Agent.PostToPageAsync(new
            {
                type = "agentWorkspace",
                reqId,
                ok = false,
                error = ex.Message
            });
        }
    }

    private Task ReplyWorkspaceAsync(string? reqId, object payload) =>
        _pages.Agent.PostToPageAsync(new
        {
            type = "agentWorkspace",
            reqId,
            ok = true,
            workspace = payload
        });

    private object BuildWorkspacePayload()
    {
        var root = AgentWorkspace.ResolveRoot(_config);
        AgentWorkspaceRecents.Touch(root);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var recents = AgentWorkspaceRecents.List()
            .Where(p => !string.Equals(p, home, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new
        {
            currentPath = root,
            currentName = AgentWorkspaceRecents.DisplayName(root),
            homePath = home,
            recents = recents.Select(p => new { path = p, name = AgentWorkspaceRecents.DisplayName(p) }).ToList(),
            defaultAgentStrategy = _config.DefaultAgentStrategy,
            agentSandboxLazyInit = _config.AgentSandboxLazyInit,
            agentModelAuto = _config.AgentModelAuto,
            agentManualModel = _config.AgentManualModel,
            agentManualProviderId = _config.AgentManualProviderId,
            agentAutoPreferProviderOrder = _config.AgentAutoPreferProviderOrder,
            agentAutoProviderOrder = _config.AgentAutoProviderOrder,
            agentDefaultProviderId = _config.AgentDefaultProviderId,
            agentModel = _config.Model
        };
    }

    private async Task HandleAgentProviderCatalogAsync(JsonElement msg)
    {
        ReloadConfig();
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        await _pages.Agent.PostToPageAsync(new
        {
            type = "agentProviderCatalog",
            reqId,
            ok = true,
            providers = AutoProviderPool.ToCatalogDto(_config),
            agentAutoPreferProviderOrder = _config.AgentAutoPreferProviderOrder,
            agentAutoProviderOrder = _config.AgentAutoProviderOrder,
            agentDefaultProviderId = _config.AgentDefaultProviderId
        });
    }

    private async Task PushWorkspaceStateAsync()
    {
        try
        {
            await _pages.Agent.PostToPageAsync(new { type = "agentWorkspaceState", workspace = BuildWorkspacePayload() });
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<string?> PickWorkspaceFolderOnUiAsync(string title = "选择 Agent 工作区文件夹")
    {
        string? picked = null;
        await RunOnUiAsync(() =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title
            };
            if (dlg.ShowDialog() == true)
                picked = AgentWorkspaceRecents.Normalize(dlg.FolderName);
            return Task.CompletedTask;
        });
        return picked;
    }

    private static string? TryReadHarnessPhase(string? harnessStateJson)
    {
        if (string.IsNullOrWhiteSpace(harnessStateJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(harnessStateJson);
            if (doc.RootElement.TryGetProperty("phase", out var phaseEl))
                return HarnessPhasePolicy.TraceLabel(Enum.Parse<HarnessPhase>(phaseEl.GetString()!, true));
        }
        catch
        {
            // ignore
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _pages.MessageReceived -= OnWebMessage;
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        _automationHost?.Dispose();
        _automationHost = null;
        await _mcpHub.DisposeAsync();
    }

    private async Task<AgentAutomationRun> ExecuteAutomationAsync(
        AgentAutomation automation,
        string triggerType,
        string? payloadJson,
        CancellationToken ct)
    {
        var run = new AgentAutomationRun
        {
            Id = "run_" + Guid.NewGuid().ToString("N")[..10],
            AutomationId = automation.Id,
            AutomationName = automation.Name,
            TriggerType = triggerType,
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = "running",
            TriggerPayloadJson = payloadJson
        };

        JsonElement? payloadEl = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                payloadEl = doc.RootElement.Clone();
            }
            catch
            {
                // ignore invalid payload
            }
        }

        var task = AgentAutomationPrompt.Render(automation.Instructions, payloadEl);
        if (string.IsNullOrWhiteSpace(task))
            task = $"[Automation: {automation.Name}] 按指令执行后台任务。";

        var prevWorkspace = _config.AgentWorkspaceRoot;
        var prevStrategy = _config.DefaultAgentStrategy;
        try
        {
            if (!string.IsNullOrWhiteSpace(automation.WorkspaceRoot))
                _config.AgentWorkspaceRoot = automation.WorkspaceRoot.Trim();

            _config.DefaultAgentStrategy = automation.Strategy ?? AgentStrategies.Execute;
            ConfigStore.Save(_config);
            AgentDesktopConfigSync.Apply(_config);

            ReloadConfig();
            await SyncApiBridgeFromApiAccountsAsync();
            if (!HasConfiguredDeepSeekApiAccount(_config))
            {
                run.Status = "failed";
                run.Error = "请在 API 管理中添加 DeepSeek 账户并填写用户 Token";
                run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return run;
            }

            AgentModeHelper.ApplyAgentDefaults(_config);
            AgentModeHelper.ApplyChatMode(_config, "专家", _config.AgentDeepThinking, _config.AgentWebSearch);
            ConfigStore.Save(_config);
            _localApi.UpdateConfig(_config);
            _localApi.EnsureAgentScopedListening();

            using var featureScope = DsdAgentApiScope.Begin(
                _config.AgentDeepThinking, _config.AgentWebSearch);
            using var debugLog = _config.AgentDebugLogEnabled
                ? AgentDebugLogger.Begin($"[automation:{automation.Name}] {task}", false)
                : null;

            var strategy = !string.IsNullOrWhiteSpace(automation.GraphId)
                ? AgentStrategies.GraphStrategy(automation.GraphId.Trim())
                : automation.Strategy ?? AgentStrategies.Execute;

            var playbookId = automation.PlaybookId;
            if (!string.IsNullOrWhiteSpace(automation.BlockPipelineId))
                playbookId = automation.BlockPipelineId.Trim();

            var harnessResult = await GetHarnessRunner().RunAsync(
                new HarnessRunRequest
                {
                    Config = _config,
                    Prompt = task,
                    Strategy = strategy,
                    PlaybookId = playbookId,
                    SkillId = automation.SkillId,
                    AgentSessionId = automation.Id
                },
                new HarnessRunCallbacks
                {
                    OnLog = line => debugLog?.Write("AUTO", line),
                    OnThinking = (text, _) => debugLog?.LogThinkingDelta(text),
                    OnAnswerDelta = (delta, _) => debugLog?.Write("ANSWER", delta)
                },
                ct);

            run.Status = "completed";
            run.Answer = harnessResult.Answer;
            run.Summary = harnessResult.Answer;
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return run;
        }
        catch (OperationCanceledException)
        {
            run.Status = "failed";
            run.Error = "已取消";
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return run;
        }
        catch (Exception ex)
        {
            run.Status = "failed";
            run.Error = ex.Message;
            run.Summary = "失败: " + ex.Message;
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return run;
        }
        finally
        {
            _localApi.ReleaseAgentScopedListening();
            _config.AgentWorkspaceRoot = prevWorkspace;
            _config.DefaultAgentStrategy = prevStrategy;
            ConfigStore.Save(_config);
        }
    }
}
