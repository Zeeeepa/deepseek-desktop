using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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

    public bool IsAgentVisible { get; private set; }
    public bool AgentPageReady { get; private set; }

    public event EventHandler<JsonElement>? MessageReceived;

    public DesktopWebHost(WebView2 chatView, WebView2 agentView)
    {
        _chatView = chatView;
        _agentView = agentView;
        Chat = new WebInjectService(chatView, WebViewPageKind.Chat);
        Agent = new WebInjectService(agentView, WebViewPageKind.Agent);
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

        var startAgent = startWorkMode is "agent" or "plan";
        if (startAgent)
            ShowAgent();
        else
            ShowChat();
    }

    public bool IsAgentHostPage => IsAgentVisible;

    public WebInjectService ActiveInject => IsAgentVisible ? Agent : Chat;

    public Task PostToPageAsync(object message) => ActiveInject.PostToPageAsync(message);

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
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        Chat.WebChatStreamAsync(messages, model, thinking, search, ct, webUserToken, webChatSessionId);

    public Task<WebChatResult> WebChatAsync(
        IReadOnlyList<ChatMessage> messages,
        string model,
        bool thinking,
        bool search,
        CancellationToken ct,
        string? webUserToken = null,
        string? webChatSessionId = null) =>
        Chat.WebChatAsync(messages, model, thinking, search, ct, webUserToken, webChatSessionId);

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
        if (url.StartsWith("https://chat.deepseek.com", StringComparison.OrdinalIgnoreCase) &&
            current.StartsWith("https://chat.deepseek.com", StringComparison.OrdinalIgnoreCase))
            return;

        core.Navigate(url);
    }

    public void ShowChat()
    {
        IsAgentVisible = false;
        _chatView.Visibility = Visibility.Visible;
        _agentView.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_chatView, 1);
        Panel.SetZIndex(_agentView, 0);
    }

    public void ShowAgent()
    {
        IsAgentVisible = true;
        _agentView.Visibility = Visibility.Visible;
        _chatView.Visibility = Visibility.Collapsed;
        Panel.SetZIndex(_agentView, 1);
        Panel.SetZIndex(_chatView, 0);
    }

    private void ForwardMessage(object? sender, JsonElement e) =>
        MessageReceived?.Invoke(this, e);
}
