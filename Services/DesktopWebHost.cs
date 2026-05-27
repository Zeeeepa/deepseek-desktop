using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 对话页与 Agent 页各用独立 WebView2，切换时仅显示/隐藏，避免反复 Navigate 导致卡顿。
/// </summary>
public sealed class DesktopWebHost : IDesktopWebHost
{
    private readonly WebView2 _chatView;
    private readonly WebView2 _agentView;
    private InjectScheduler? _injectScheduler;

    IDdPageMessenger IDesktopWebHost.Chat => Chat;
    IDdPageMessenger IDesktopWebHost.Agent => Agent;

    public WebInjectService Chat { get; }
    public WebInjectService Agent { get; }
    public WorkModeCoordinator WorkMode { get; }

    public bool IsAgentVisible { get; private set; }
    public bool AgentPageReady { get; private set; }
    public bool IsSurfaceSwitching { get; private set; }

    public event EventHandler<JsonElement>? MessageReceived;

    /// <summary>聊天 / Agent 表面切换后触发（用于 WPF 原生模式按钮等）。</summary>
    public event Action? SurfaceChanged;

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

    public void CancelActiveWebChatStreams() => Chat.CancelActiveWebChatStreams();

    public void InitializeInjectScheduler(Dispatcher dispatcher)
    {
        _injectScheduler = new InjectScheduler(
            dispatcher,
            (ct, forceReset) => RunScheduledChatInjectAsync(ct, forceReset),
            () => IsSurfaceSwitching);
    }

    public void RequestChatInject(string reason, bool forceReset = false) =>
        _injectScheduler?.Request(reason, forceReset);

    public void CancelChatInject() => _injectScheduler?.Cancel();

    public async Task InitializeAsync(CoreWebView2Environment env, string? startWorkMode)
    {
        _agentView.AllowExternalDrop = false;

        await _chatView.EnsureCoreWebView2Async(env);
        await _agentView.EnsureCoreWebView2Async(env);

        _agentView.AllowExternalDrop = false;

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

    public string? AgentSource => _agentView.CoreWebView2?.Source;

    public WebInjectService ActiveInject => IsAgentVisible ? Agent : Chat;

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
            : new[] { 150, 500 };
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
        HostFireAndForget.Run(
            () => BroadcastWorkModeStateAsync(includeImmediate: false, ct),
            "workModeBroadcastRetries");

    public Task PushAgentAuthHintAsync(bool loggedIn) => Agent.PushAgentAuthHintAsync(loggedIn);

    public IReadOnlyList<string> AgentRefFileIds
    {
        get => ActiveInject.AgentRefFileIds;
        set => ActiveInject.AgentRefFileIds = value;
    }

    public Task SyncApiBridgeTokenAsync(string? token) => Chat.SyncApiBridgeTokenAsync(token);

    public Task EnsureApiBridgeReadyAsync(CancellationToken ct = default) =>
        Chat.EnsureApiBridgeReadyAsync(ct);

    public Task<DsdApiHealth?> ProbeDsdApiHealthAsync(string? configWebUserToken, string baseUrl,
        CancellationToken ct = default) =>
        Chat.ProbeDsdApiHealthAsync(configWebUserToken, baseUrl, ct);

    public Task<string?> TryReadUserTokenAsync() => Chat.TryReadUserTokenAsync();

    public Task<string?> GetUserTokenAsync(bool waitForBridge = true) =>
        Chat.GetUserTokenAsync(waitForBridge);

    public Task TriggerChatInjectAsync(bool forceReset = false) =>
        Chat.TriggerInjectAsync(forceReset);

    public Task RunScheduledChatInjectAsync(CancellationToken ct = default, bool forceReset = false) =>
        Chat.RunScheduledInjectAsync(ct, forceReset);

    public Task BurstChatInjectAsync(CancellationToken ct = default, bool forceReset = false) =>
        RunScheduledChatInjectAsync(ct, forceReset);

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
        if (ChatNavigationPolicy.SameChatLocation(current, url)) return Task.CompletedTask;

        core.Navigate(url);
        return Task.CompletedTask;
    }

    public Task NavigateChatUrlIfNeededAsync(string url)
    {
        RunOnUiSync(() =>
        {
            var core = _chatView.CoreWebView2;
            if (core is null) return;
            var current = core.Source ?? "";
            if (ChatNavigationPolicy.SameChatLocation(current, url)) return;
            core.Navigate(url);
        });
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
        if (ChatNavigationPolicy.SameChatLocation(current, url)) return;

        core.Navigate(url);
    }

    public void ShowChat()
    {
        RunOnUiSync(() =>
        {
            IsAgentVisible = false;
            Panel.SetZIndex(_chatView, 1);
            Panel.SetZIndex(_agentView, 0);
            CrossFadeTo(_chatView, _agentView, "chat");
            WorkModeTrace.Write("ShowChat: chat visible");
            SurfaceChanged?.Invoke();
            _ = Chat.EnsureChatModeFloaterAsync();
            _chatView.Focus();
        });
    }

    public void ShowAgent()
    {
        RunOnUiSync(() =>
        {
            IsAgentVisible = true;
            Panel.SetZIndex(_agentView, 1);
            Panel.SetZIndex(_chatView, 0);
            CrossFadeTo(_agentView, _chatView, "agent");
            _agentView.Focus();
            WorkModeTrace.Write("ShowAgent: agent visible");
            SurfaceChanged?.Invoke();
        });
    }

    private void CrossFadeTo(UIElement show, UIElement hide, string target)
    {
        const int ms = 120;
        IsSurfaceSwitching = true;
        DesktopUiTrace.CrossFadeStart(target);

        hide.BeginAnimation(UIElement.OpacityProperty, null);
        show.BeginAnimation(UIElement.OpacityProperty, null);

        hide.Visibility = Visibility.Visible;
        show.Visibility = Visibility.Visible;

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeOut.Completed += (_, _) =>
        {
            hide.Visibility = Visibility.Collapsed;
            hide.Opacity = 1;
        };
        hide.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        show.Opacity = 0;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(ms))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        fadeIn.Completed += (_, _) => IsSurfaceSwitching = false;
        show.BeginAnimation(UIElement.OpacityProperty, fadeIn);
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

    /// <summary>
    /// 将文件夹拖放绑定到宿主容器（勿绑 WebView2 本身，否则会触发 Delegate 类型冲突导致启动失败）。
    /// </summary>
    public void AttachAgentDragDrop(UIElement surface)
    {
        if (surface == null) return;
        surface.AllowDrop = true;
        surface.PreviewDragOver -= OnAgentDragSurfaceOver;
        surface.PreviewDrop -= OnAgentDragSurfaceDrop;
        surface.PreviewDragOver += OnAgentDragSurfaceOver;
        surface.PreviewDrop += OnAgentDragSurfaceDrop;
    }

    private void OnAgentDragSurfaceOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!IsAgentVisible)
            return;
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;
        e.Effects = System.Windows.DragDropEffects.Copy;
        e.Handled = true;
        _ = Agent.PostToPageAsync(new { type = "agentDragHover", active = true });
    }

    private void OnAgentDragSurfaceDrop(object sender, System.Windows.DragEventArgs e)
    {
        _ = Agent.PostToPageAsync(new { type = "agentDragHover", active = false });
        if (!IsAgentVisible)
            return;
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] raw || raw.Length == 0)
            return;
        e.Handled = true;
        var paths = raw
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0)
            return;
        _ = Agent.PostToPageAsync(new { type = "agentDroppedPaths", paths });
    }

    private void ForwardMessage(object? sender, JsonElement e) =>
        MessageReceived?.Invoke(this, e);
}
