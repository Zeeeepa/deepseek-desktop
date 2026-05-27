using System.Windows;
using System.Windows.Threading;
using DeepSeekBrowser.Services.ApiManagement;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekBrowser.Services;

/// <summary>Wires WebView2 hosts, API stack, and agent shell during main window startup.</summary>
public sealed class DesktopCompositionRoot
{
    private readonly WebView2 _chatWebView;
    private readonly WebView2 _agentWebView;
    private readonly WebView2 _bridgeWebView;
    private readonly Dispatcher _dispatcher;
    private readonly Action _onWorkModeSurfaceChanged;
    private readonly Func<Window?> _owner;

    public DesktopWebHost? WebHost { get; private set; }
    public WebChatBridgeHost? ApiBridgeHost { get; private set; }
    public LocalOpenAiServer? LocalApi { get; private set; }
    public DesktopAgentHost? AgentHost { get; private set; }

    public DesktopCompositionRoot(
        WebView2 chatWebView,
        WebView2 agentWebView,
        WebView2 bridgeWebView,
        Dispatcher dispatcher,
        Action onWorkModeSurfaceChanged,
        Func<Window?> owner)
    {
        _chatWebView = chatWebView;
        _agentWebView = agentWebView;
        _bridgeWebView = bridgeWebView;
        _dispatcher = dispatcher;
        _onWorkModeSurfaceChanged = onWorkModeSurfaceChanged;
        _owner = owner;
    }

    public async Task InitializeAsync(
        CoreWebView2Environment env,
        Func<string, Task> navigateWebAsync,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _chatWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var chatCoreForCache = _chatWebView.CoreWebView2
            ?? throw new InvalidOperationException("Chat WebView2 core is not ready.");
        await EmbeddedUiCacheService.EnsureFreshUiAsync(chatCoreForCache).ConfigureAwait(true);

        ApiBridgeHost = new WebChatBridgeHost(_bridgeWebView);
        await ApiBridgeHost.AttachAndNavigateAsync(env).ConfigureAwait(true);

        var core = _chatWebView.CoreWebView2
            ?? throw new InvalidOperationException("Chat WebView2 core is not ready.");

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsWebMessageEnabled = true;

        WebHost = new DesktopWebHost(_chatWebView, _agentWebView);
        WebHost.InitializeInjectScheduler(_dispatcher);
        WebHost.SurfaceChanged += _onWorkModeSurfaceChanged;
        WebHost.AttachApiBridge(ApiBridgeHost);

        LocalApi = new LocalOpenAiServer(WebHost.Chat);
        AgentHarnessRequestLogRecorder.Register();
        AgentHost = new DesktopAgentHost(WebHost, LocalApi);
        if (_owner() is { } ownerWindow)
            AgentHost.SetOwner(ownerWindow);
        AgentHost.NavigateToUrl = navigateWebAsync;
        AgentHost.Start();
        ShutdownCoordinator.Register(AgentHost, LocalApi);

        var savedConfig = ConfigStore.Load();
        var bridgeToken = AccountCredentials.ResolveWebUserTokenForRoute(null, savedConfig, "deepseek");
        if (!string.IsNullOrWhiteSpace(bridgeToken))
            await ApiBridgeHost.SyncWebUserTokenAsync(bridgeToken).ConfigureAwait(true);

        var config = ConfigStore.Load();
        await WebHost.InitializeAsync(env, config.DefaultWorkMode).ConfigureAwait(true);

        var agentCore = _agentWebView.CoreWebView2;
        if (agentCore is not null)
            agentCore.Settings.IsWebMessageEnabled = true;
    }

    public void AttachMainWindowHandlers(
        EventHandler<CoreWebView2NavigationStartingEventArgs> chatNavigationStarting,
        EventHandler<CoreWebView2NavigationCompletedEventArgs> chatNavigationCompleted,
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? agentNavigationCompleted,
        EventHandler<object>? spaHistoryChanged,
        EventHandler<CoreWebView2SourceChangedEventArgs>? spaSourceChanged,
        EventHandler<object>? documentTitleChanged)
    {
        var core = _chatWebView.CoreWebView2;
        if (core is null) return;

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            MainWindowNavigation.OpenExternal(e.Uri);
        };
        core.NavigationStarting += chatNavigationStarting;
        core.NavigationCompleted += chatNavigationCompleted;
        if (spaHistoryChanged is not null)
            core.HistoryChanged += spaHistoryChanged;
        if (spaSourceChanged is not null)
            core.SourceChanged += spaSourceChanged;

        if (documentTitleChanged is not null)
            core.DocumentTitleChanged += documentTitleChanged;

        var agentCore = _agentWebView.CoreWebView2;
        if (agentCore is not null && agentNavigationCompleted is not null)
            agentCore.NavigationCompleted += agentNavigationCompleted;
        if (agentCore is not null && documentTitleChanged is not null)
            agentCore.DocumentTitleChanged += documentTitleChanged;
    }

    public void AttachAgentDragDrop(UIElement surface) =>
        WebHost?.AttachAgentDragDrop(surface);
}

internal static class MainWindowNavigation
{
    public static void OpenExternal(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
