using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using Microsoft.UI.Xaml.Controls;

namespace DeepSeek.Desktop.Services;

/// <summary>处理 chat.deepseek.com 注入脚本 postMessage，驱动 WinUI 导航与 Token 同步。</summary>
public sealed class ChatMessageRouter
{
    private readonly McpHub _mcpHub = new();

    public void Attach(WinUiWebInjectService chatInject)
    {
        chatInject.MessageReceived += OnMessage;
    }

    private async void OnMessage(object? sender, JsonElement msg)
    {
        if (!msg.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();

        switch (type)
        {
            case "nativeReady":
            case "requestWorkModeState":
                await PushWorkModeStateAsync();
                _ = RefreshLoginAsync();
                if (!string.IsNullOrWhiteSpace(ConfigStore.Load().WebUserToken))
                    _ = ConnectMcpAsync();
                break;
            case "syncToken":
                if (msg.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
                {
                    var t = tok.GetString();
                    if (!string.IsNullOrWhiteSpace(t))
                        await ApplyTokenAsync(t);
                }
                break;
            case "refreshLoginState":
                await RefreshLoginAsync();
                break;
            case "setWorkMode":
                if (msg.TryGetProperty("mode", out var modeEl))
                    await ApplyWorkModeAsync(modeEl.GetString());
                break;
            case "toggleWorkMode":
                await ApplyWorkModeAsync(App.MainWindow?.IsAgentSelected == true ? "chat" : "agent");
                break;
            case "navigateToAgent":
                await ApplyWorkModeAsync("agent");
                break;
            case "navigateToChat":
                await ApplyWorkModeAsync("chat");
                break;
            case "openSettings":
                App.MainWindow?.NavigateToSettings();
                break;
        }
    }

    private static async Task ApplyWorkModeAsync(string? mode)
    {
        mode = WorkModeModes.NormalizeMode(mode);
        var cfg = ConfigStore.Load();
        cfg.DefaultWorkMode = mode;
        ConfigStore.Save(cfg);
        AppHost.Instance.ReloadConfig();

        if (mode is "agent" or "plan")
            App.MainWindow?.NavigateToAgent();
        else
            App.MainWindow?.NavigateToChat();

        await PushWorkModeStateAsync();
    }

    private static async Task PushWorkModeStateAsync()
    {
        var cfg = ConfigStore.Load();
        var surface = App.MainWindow?.IsAgentSelected == true ? "agent" : "chat";
        var inject = AppHost.Instance.ChatInject;
        if (inject is null) return;
        await inject.PushWorkModeStateAsync(WorkModeStatePayload.For(cfg.DefaultWorkMode, surface == "agent"));
    }

    private static async Task ApplyTokenAsync(string token)
    {
        var normalized = token.Trim();
        var cfg = ConfigStore.Load();
        if (normalized == cfg.WebUserToken) return;

        cfg.WebUserToken = normalized;
        ConfigStore.Save(cfg);
        AppHost.Instance.SaveConfig(cfg);
        AgentDesktopConfigSync.Apply(cfg);

        var inject = AppHost.Instance.ChatInject;
        if (inject is not null)
            await inject.SyncApiBridgeTokenAsync(normalized);
        await AppHost.Instance.EnsureStackLinkedAsync();
    }

    private static async Task RefreshLoginAsync()
    {
        var inject = AppHost.Instance.ChatInject;
        if (inject is null) return;

        try
        {
            var cfg = ConfigStore.Load();
            if (!string.IsNullOrWhiteSpace(cfg.WebUserToken))
                await inject.SyncApiBridgeTokenAsync(cfg.WebUserToken);
            await AppHost.Instance.EnsureStackLinkedAsync();
            await inject.PostToPageAsync(new
            {
                type = "loginState",
                loggedIn = !string.IsNullOrWhiteSpace(ConfigStore.Load().WebUserToken)
            });
        }
        catch
        {
            // ignore
        }
    }

    private async Task ConnectMcpAsync()
    {
        var cfg = ConfigStore.Load();
        await _mcpHub.ConnectEnabledAsync(cfg.McpServers, _ => { }, CancellationToken.None);
    }
}
