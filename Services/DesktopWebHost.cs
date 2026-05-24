using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 对话页与 Agent 页各用独立 WebView2，切换时仅显示/隐藏，避免反复 Navigate 导致卡顿。
/// </summary>
public sealed class DesktopWebHost
{
    private readonly WebView2 _chatView;
    private readonly WebView2 _agentView;

    public WebInjectService Chat { get; }
    public WebInjectService Agent { get; }
    public WorkModeCoordinator WorkMode { get; }

    public bool IsAgentVisible { get; private set; }
    public bool AgentPageReady { get; private set; }

    public event EventHandler<JsonElement>? MessageReceived;

    public DesktopWebHost(WebView2 chatView, WebView2 agentView)
    {
        _chatView = chatView;
        _agentView = agentView;
        Chat = new WebInjectService(chatView, WebViewPageKind.Chat);
        Agent = new WebInjectService(agentView, WebViewPageKind.Agent);
        WorkMode = new WorkModeCoordinator(this);
        Chat.MessageReceived += ForwardMessage;
        Agent.MessageReceived += ForwardMessage;
    }

    public void AttachApiBridge(WebChatBridgeHost bridge)
    {
        Chat.AttachApiBridge(bridge);
        Agent.AttachApiBridge(bridge);
    }

    public async Task InitializeAsync(CoreWebView2Environment env, string? startWorkMode)
    {
        await _chatView.EnsureCoreWebView2Async(env);
        await _agentView.EnsureCoreWebView2Async(env);

        var chatCore = _chatView.CoreWebView2!;
        var agentCore = _agentView.CoreWebView2!;

        agentCore.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess)
                AgentPageReady = true;
        };

        await Chat.AttachAsync(chatCore);
        await Agent.AttachAsync(agentCore);

        agentCore.Navigate(AppNavigation.AgentPageUrl);
        chatCore.Navigate(AppNavigation.DeepSeekUrl);

        WorkMode.SetModeFromConfig(startWorkMode);
        if (WorkMode.IsAgentLike)
            await WorkMode.ShowAgentSurfaceAsync();
        else
            await WorkMode.ShowChatSurfaceAsync();
        await WorkMode.BroadcastAsync();
    }

    public bool IsAgentHostPage => IsAgentVisible;

    public string? AgentSource => _agentView.CoreWebView2?.Source;

    public WebInjectService ActiveInject => IsAgentVisible ? Agent : Chat;

    public Task NavigateAgentAsync(string url)
    {
        RunOnUiSync(() =>
        {
            ShowAgent();
            var core = _agentView.CoreWebView2;
            if (core is null) return;
            var current = core.Source ?? "";
            if (!string.Equals(current, url, StringComparison.OrdinalIgnoreCase))
                core.Navigate(url);
        });
        return Task.CompletedTask;
    }

    public Task PostToPageAsync(object message) => ActiveInject.PostToPageAsync(message);

    /// <summary>立即向当前可见页推送 workModeState，另一页在后台补发（用于交互切换，避免等待重试循环）。</summary>
    public async Task PushWorkModeStateNowAsync(WorkModeStatePayload state, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (IsAgentVisible)
        {
            await Agent.PushWorkModeStateAsync(state);
            _ = Chat.PushWorkModeStateAsync(state);
        }
        else
        {
            await Chat.PushWorkModeStateAsync(state);
            _ = Agent.PushWorkModeStateAsync(state);
        }
    }

    /// <summary>向双 WebView 广播 workModeState（每轮按当前可见页重建状态，避免切换后迟到的旧快照把按钮闪回「普通」）。</summary>
    public Task BroadcastWorkModeStateAsync(CancellationToken ct = default) =>
        BroadcastWorkModeStateAsync(includeImmediate: true, ct);

    public async Task BroadcastWorkModeStateAsync(bool includeImmediate, CancellationToken ct = default)
    {
        var delays = includeImmediate
            ? new[] { 0, 50, 150, 350, 700, 1200 }
            : new[] { 50, 150, 350, 700 };
        foreach (var delay in delays)
        {
            ct.ThrowIfCancellationRequested();
            if (delay > 0)
                await Task.Delay(delay, ct);
            var state = WorkMode.BuildState();
            await Chat.PushWorkModeStateAsync(state);
            await Agent.PushWorkModeStateAsync(state);
        }
    }

    public void ScheduleWorkModeBroadcastRetries(CancellationToken ct = default) =>
        _ = BroadcastWorkModeStateAsync(includeImmediate: false, ct);

    public Task AfterShowChatAsync(string workMode, CancellationToken ct = default)
    {
        WorkMode.SetModeFromConfig(workMode);
        return WorkMode.ShowChatSurfaceAsync(ct);
    }

    public Task PushAgentAuthHintAsync(bool loggedIn) => Agent.PushAgentAuthHintAsync(loggedIn);

    public IReadOnlyList<string> AgentRefFileIds
    {
        get => ActiveInject.AgentRefFileIds;
        set => ActiveInject.AgentRefFileIds = value;
    }

    public Task SyncApiBridgeTokenAsync(string? token) => Chat.SyncApiBridgeTokenAsync(token);

    public Task EnsureApiBridgeReadyAsync(CancellationToken ct = default) =>
        Chat.EnsureApiBridgeReadyAsync(ct);

    public Task<Chat2ApiHealth> ProbeChat2ApiHealthAsync(string? configWebUserToken, string baseUrl,
        CancellationToken ct = default) =>
        Chat.ProbeChat2ApiHealthAsync(configWebUserToken, baseUrl, ct);

    public Task<string?> TryReadUserTokenAsync() => Chat.TryReadUserTokenAsync();

    public Task<string?> GetUserTokenAsync(bool waitForBridge = true) =>
        Chat.GetUserTokenAsync(waitForBridge);

    public Task TriggerChatInjectAsync(bool forceReset = false) =>
        Chat.TriggerInjectAsync(forceReset);

    public Task BurstChatInjectAsync(CancellationToken ct = default, bool forceReset = false) =>
        Chat.BurstInjectAsync(ct, forceReset);

    public IAsyncEnumerable<WebChatStreamEvent> WebChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null)
    {
        var inject = ActiveInject;
        var prev = inject.AgentRefFileIds;
        inject.AgentRefFileIds = refFileIds;
        try
        {
            return inject.WebChatStreamAsync(
                messages, model, thinking, search, ct, webUserToken, webChatSessionId, allowToolCalls);
        }
        finally
        {
            inject.AgentRefFileIds = prev;
        }
    }

    public Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null)
    {
        var inject = ActiveInject;
        var prev = inject.AgentRefFileIds;
        inject.AgentRefFileIds = refFileIds;
        try
        {
            return inject.WebChatAsync(
                messages, model, thinking, search, ct, webUserToken, webChatSessionId, allowToolCalls);
        }
        finally
        {
            inject.AgentRefFileIds = prev;
        }
    }

    public IAsyncEnumerable<WebChatStreamEvent> WebChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        WebChatStreamAsync(messages, model, thinking, search, AgentRefFileIds, false, ct, webUserToken, webChatSessionId);

    public Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        WebChatAsync(messages, model, thinking, search, AgentRefFileIds, false, ct, webUserToken, webChatSessionId);

    public Task SyncChatSessionAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return Task.CompletedTask;
        var url = AppNavigation.ChatSessionUrl(sessionId);
        var core = _chatView.CoreWebView2;
        if (core is null) return Task.CompletedTask;

        var current = core.Source ?? "";
        if (SameChatLocation(current, url)) return Task.CompletedTask;

        core.Navigate(url);
        return Task.CompletedTask;
    }

    public async Task SwitchToUrlAsync(string url)
    {
        if (AppNavigation.IsAgentPage(url))
        {
            ShowAgent();
            return;
        }

        ShowChat();
        var core = _chatView.CoreWebView2;
        if (core is null) return;

        var current = core.Source ?? "";
        if (SameChatLocation(current, url)) return;

        core.Navigate(url);
    }

    private static bool SameChatLocation(string current, string target)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(target))
            return false;

        try
        {
            var c = new Uri(current);
            var t = new Uri(target);
            if (!c.Host.Equals(t.Host, StringComparison.OrdinalIgnoreCase))
                return false;

            var cp = c.AbsolutePath.TrimEnd('/');
            var tp = t.AbsolutePath.TrimEnd('/');
            return string.Equals(cp, tp, StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public void ShowChat()
    {
        RunOnUiSync(() =>
        {
            IsAgentVisible = false;
            _chatView.Visibility = Visibility.Visible;
            _agentView.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(_chatView, 1);
            Panel.SetZIndex(_agentView, 0);
            WorkModeTrace.Write("ShowChat: chat visible");
        });
    }

    public void ShowAgent()
    {
        RunOnUiSync(() =>
        {
            IsAgentVisible = true;
            _agentView.Visibility = Visibility.Visible;
            _chatView.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(_agentView, 1);
            Panel.SetZIndex(_chatView, 0);
            WorkModeTrace.Write("ShowAgent: agent visible");
        });
    }

    private static void RunOnUiSync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action, DispatcherPriority.Send);
    }

    private void ForwardMessage(object? sender, JsonElement e) =>
        MessageReceived?.Invoke(this, e);
}
