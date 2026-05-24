using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace DeepSeek.Desktop.Services;

public sealed class AppHost
{
    public static AppHost Instance { get; } = new();

    private CoreWebView2Environment? _env;
    private WinUiWebChatBridgeHost? _bridge;
    private WinUiWebInjectService? _chatInject;
    private LocalOpenAiServer? _externalApi;
    private readonly ChatMessageRouter _chatRouter = new();
    private AppConfig _config = new();

    public ChatMessageRouter ChatRouter => _chatRouter;

    public AppConfig Config => _config;
    public WinUiWebInjectService? ChatInject => _chatInject;
    public WinUiWebChatBridgeHost? ChatBridge => _bridge;
    public LocalOpenAiServer? ExternalApi => _externalApi;

    public async Task InitializeAsync(WebView2 chatWebView, WebView2 bridgeWebView)
    {
        _config = ConfigStore.Load();
        var userData = DeepSeekDesktopApp.WebViewUserDataDirectory;

        Directory.CreateDirectory(userData);
        var options = new CoreWebView2EnvironmentOptions();
        _env = await CoreWebView2Environment.CreateWithOptionsAsync(
            null, userData, options);

        await bridgeWebView.EnsureCoreWebView2Async(_env);
        await chatWebView.EnsureCoreWebView2Async(_env);

        _bridge = new WinUiWebChatBridgeHost(bridgeWebView);
        await _bridge.AttachAndNavigateAsync(_env);

        _chatInject = new WinUiWebInjectService(chatWebView, WebViewPageKind.Chat);
        _chatInject.AttachApiBridge(_bridge);
        await _chatInject.AttachAsync(chatWebView.CoreWebView2!);

        _externalApi = new LocalOpenAiServer(_chatInject);
        _externalApi.UpdateConfig(_config);

        _chatRouter.Attach(_chatInject);

        await DesktopEmbeddedStack.EnsureLinkedAsync(_config, _chatInject);

        if (_config.EnableExternalOpenAiApi)
            _externalApi.EnsureExternalApiListening();

        if (!string.IsNullOrWhiteSpace(_config.WebUserToken))
            await _bridge.SyncWebUserTokenAsync(_config.WebUserToken);
    }

    public void ReloadConfig() => _config = ConfigStore.Load();

    public void SaveConfig(AppConfig config)
    {
        _config = config;
        ConfigStore.Save(_config);
        _externalApi?.UpdateConfig(_config);
        if (_config.EnableExternalOpenAiApi)
            _externalApi?.EnsureExternalApiListening();
        else
            _externalApi?.Stop();
    }

    public Task EnsureStackLinkedAsync(CancellationToken ct = default) =>
        _chatInject is null
            ? Task.CompletedTask
            : DesktopEmbeddedStack.EnsureLinkedAsync(_config, _chatInject, ct);

    public void BeginAgentLlmBridge()
    {
        _externalApi?.EnsureAgentScopedListening();
        AgentDesktopConfigSync.Apply(_config);
    }

    public void EndAgentLlmBridge()
    {
        _externalApi?.ReleaseAgentScopedListening();
        AgentDesktopConfigSync.Apply(_config);
    }
}
