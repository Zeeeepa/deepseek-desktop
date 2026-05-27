using System.Windows;
using System.Windows.Threading;
using DeepSeekBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekBrowser;

public partial class MainWindow
{
    private void OnWorkModeSurfaceChanged()
    {
        if (_webHost is null || !_webViewReady) return;

        if (_agentHost?.IsApplyingWorkMode == true)
        {
            _ = _webHost.Chat.EnsureChatModeFloaterAsync();
            return;
        }

        RemindWebModeFloaterLight();
    }

    private void RemindWebModeFloaterLight()
    {
        if (_webHost is null) return;

        const string chatScript = ChatModeFloaterScript.Ensure
            + "(function(){try{"
            + "if(window.DsWorkMode&&window.DsWorkMode.flushPending)window.DsWorkMode.flushPending();"
            + "}catch(e){}})();";

        const string agentScript =
            "(function(){try{"
            + "var m=document.getElementById('mode-float');"
            + "if(m)m.style.removeProperty('display');"
            + "if(window.DsWorkMode&&window.DsWorkMode.flushPending)window.DsWorkMode.flushPending();"
            + "}catch(e){}})();";

        _ = _webHost.Chat.EvaluateOnPageAsync(chatScript);
        _ = _webHost.Agent.EvaluateOnPageAsync(agentScript);
    }

    private void OnChatNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _webHost?.CancelChatInject();
        var current = WebView.CoreWebView2?.Source;
        if (!ChatNavigationPolicy.ShouldShowLoadingOverlay(current, e.Uri, e.IsUserInitiated))
            return;

        LoadingOverlay.Visibility = Visibility.Visible;
        DesktopUiTrace.LoadingOverlayShow("chat_navigation");
    }

    private async void OnChatNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (LoadingOverlay.Visibility == Visibility.Visible)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            DesktopUiTrace.LoadingOverlayHide("chat_navigation");
        }

        if (!e.IsSuccess && WebView.CoreWebView2 is not null)
        {
            Title = $"{AppNavigation.BrandWindowTitle} · 加载失败";
            return;
        }

        if (_webHost is { IsAgentVisible: false })
        {
            if (_agentHost is not null)
                await _agentHost.OnChatNavigationCompletedAsync();
            _webHost.RequestChatInject("chat_navigation_completed", forceReset: false);
            OnWorkModeSurfaceChanged();
        }
    }

    private async void OnAgentNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _agentHost is null) return;
        if (_webHost is { IsAgentVisible: true })
        {
            await _agentHost.OnAgentNavigationCompletedAsync();
            OnWorkModeSurfaceChanged();
            if (Dispatcher.CheckAccess())
                RefreshAgentNativeDropTargets();
            else
                await Dispatcher.InvokeAsync(RefreshAgentNativeDropTargets, DispatcherPriority.Background);
        }
    }

    private async Task NavigateWebAsync(string url)
    {
        if (!_webViewReady || _webHost is null) return;

        if (AppNavigation.IsAgentPage(url))
        {
            if (!_webHost.IsAgentVisible)
            {
                await _webHost.WorkMode.ShowAgentSurfaceAsync();
                await _webHost.WorkMode.BroadcastImmediateAsync();
            }

            await _webHost.SwitchToUrlAsync(url);
            if (_agentHost is not null)
            {
                _ = _agentHost.SyncApiBridgeFromApiAccountsAsync();
                _ = _agentHost.OnAgentNavigationCompletedAsync();
            }
            return;
        }

        if (!_webHost.IsAgentVisible)
        {
            await _webHost.NavigateChatUrlIfNeededAsync(url);
            return;
        }

        await _webHost.WorkMode.ShowChatSurfaceAsync();
        await _webHost.WorkMode.BroadcastImmediateAsync();
        await _webHost.SwitchToUrlAsync(url);
    }

    private void OnSpaHistoryChanged(object? sender, object e) =>
        OnSpaNavigation();

    private void OnSpaSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e) =>
        OnSpaNavigation();

    private void OnSpaNavigation()
    {
        if (_webHost is { IsAgentVisible: true })
            return;

        DesktopUiTrace.SpaRoute("history");
        _webHost?.RequestChatInject("spa_route", forceReset: false);
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        var core = sender as CoreWebView2 ?? WebView.CoreWebView2;
        if (_webHost is { IsAgentVisible: true } && core != AgentWebView.CoreWebView2)
            return;
        if (_webHost is { IsAgentVisible: false } && core != WebView.CoreWebView2)
            return;

        if (_webHost is { IsAgentVisible: true })
        {
            var agentTitle = core?.DocumentTitle?.Trim();
            Title = string.IsNullOrWhiteSpace(agentTitle)
                ? AppNavigation.BrandWindowTitle
                : agentTitle;
            return;
        }

        Title = AppNavigation.BrandWindowTitle;
    }
}
