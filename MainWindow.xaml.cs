using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Views;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekBrowser;

public partial class MainWindow : System.Windows.Window
{
    public const string DeepSeekUrl = AppNavigation.DeepSeekUrl;
    public static string AgentPageUrl => AppNavigation.AgentPageUrl;
    private static readonly string UserDataFolder = DeepSeekDesktopApp.WebViewUserDataDirectory;

    private bool _webViewReady;
    private bool _isExiting;
    private bool _exitCleanupDone;
    private DesktopWebHost? _webHost;
    private WebChatBridgeHost? _apiBridgeHost;
    private LocalOpenAiServer? _localApi;
    private DesktopAgentHost? _agentHost;
    private TrayIconService? _tray;
    private CancellationTokenSource? _injectBurstCts;

    public MainWindow()
    {
        InitializeComponent();
        _tray = new TrayIconService(this, ExitApplication);
    }

    private void ApplyWindowIconInternal()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "deepseek.ico"),
            Path.Combine(AppContext.BaseDirectory, "deepseek.ico")
        };

        foreach (var iconPath in candidates)
        {
            if (!File.Exists(iconPath)) continue;
            try
            {
                using var stream = File.OpenRead(iconPath);
                Icon = BitmapFrame.Create(stream);
                return;
            }
            catch
            {
                // try next path
            }
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowIconInternal();

        try
        {
            Directory.CreateDirectory(UserDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder);

            await WebView.EnsureCoreWebView2Async(env);

            _apiBridgeHost = new WebChatBridgeHost(BridgeWebView);
            await _apiBridgeHost.AttachAndNavigateAsync(env);

            var core = WebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.IsWebMessageEnabled = true;

            _webHost = new DesktopWebHost(WebView, AgentWebView);
            _webHost.AttachApiBridge(_apiBridgeHost);
            _localApi = new LocalOpenAiServer(_webHost.Chat);
            _agentHost = new DesktopAgentHost(_webHost, _localApi);
            _agentHost.SetOwner(this);
            _agentHost.NavigateToUrl = NavigateWebAsync;
            _agentHost.Start();
            ShutdownCoordinator.Register(_agentHost, _localApi);

            var savedConfig = ConfigStore.Load();
            if (!string.IsNullOrWhiteSpace(savedConfig.WebUserToken))
                await _apiBridgeHost.SyncWebUserTokenAsync(savedConfig.WebUserToken);

            var config = ConfigStore.Load();
            await _webHost.InitializeAsync(env, config.DefaultWorkMode);

            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnChatNavigationStarting;
            core.NavigationCompleted += OnChatNavigationCompleted;
            core.HistoryChanged += OnSpaNavigation;
            core.SourceChanged += OnSpaNavigation;
            core.DocumentTitleChanged += OnDocumentTitleChanged;

            var agentCore = AgentWebView.CoreWebView2;
            if (agentCore is not null)
            {
                agentCore.Settings.IsWebMessageEnabled = true;
                agentCore.NavigationCompleted += OnAgentNavigationCompleted;
                agentCore.DocumentTitleChanged += OnDocumentTitleChanged;
            }

            _webViewReady = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;

            if (DeepSeekDesktopApp.IsEnvEnabled(
                    DeepSeekDesktopApp.VerifyWorkModeEnvVar,
                    DeepSeekDesktopApp.LegacyVerifyWorkModeEnvVar))
                ScheduleWorkModeSelfTest();
            else if (DeepSeekDesktopApp.IsEnvEnabled(
                         DeepSeekDesktopApp.VerifyAgentTaskEnvVar,
                         DeepSeekDesktopApp.VerifyAgentTaskEnvVar))
                ScheduleAgentTaskSelfTest();
            else if (DeepSeekDesktopApp.IsEnvEnabled(
                         DeepSeekDesktopApp.VerifyAgentEnvVar,
                         DeepSeekDesktopApp.LegacyVerifyAgentEnvVar))
                ScheduleAgentHelloSelfTest();
            else if (DeepSeekDesktopApp.IsEnvEnabled(
                         DeepSeekDesktopApp.VerifyShutdownEnvVar,
                         DeepSeekDesktopApp.VerifyShutdownEnvVar))
                ScheduleShutdownVerifyExit();
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            Views.DsMessageDialog.Warning(
                this,
                $"无法初始化 Edge 内核 (WebView2)。\n\n{ex.Message}\n\n请安装 Microsoft Edge WebView2 运行时：\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "DeepSeek");
            _isExiting = true;
            Close();
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isExiting)
        {
            e.Cancel = false;
            PerformExitCleanup();
            return;
        }

        e.Cancel = true;

        var dlg = new ClosePromptWindow { Owner = this };
        if (dlg.ShowDialog() != true)
            return;

        if (dlg.Choice == ClosePromptWindow.CloseChoice.MinimizeToTray)
        {
            Hide();
            _tray?.ShowInTray();
            return;
        }

        if (dlg.Choice == ClosePromptWindow.CloseChoice.ExitProcess)
        {
            _isExiting = true;
            e.Cancel = false;
            PerformExitCleanup();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isExiting)
        {
            PerformExitCleanup();
            System.Windows.Application.Current.Shutdown();
        }

        base.OnClosed(e);
    }

    private void PerformExitCleanup()
    {
        if (_exitCleanupDone) return;
        _exitCleanupDone = true;

        _injectBurstCts?.Cancel();
        _injectBurstCts?.Dispose();
        _injectBurstCts = null;

        _tray?.Dispose();
        _tray = null;

        _agentHost = null;
        ShutdownCoordinator.RunExitCleanup();
        _localApi = null;
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;
        PerformExitCleanup();
        Close();
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private void OnChatNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _injectBurstCts?.Cancel();
        if (e.Uri.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase))
            LoadingOverlay.Visibility = Visibility.Visible;
    }

    private async void OnChatNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (!e.IsSuccess && WebView.CoreWebView2 is not null)
        {
            Title = "DeepSeek - 加载失败";
            return;
        }

        if (_webHost is { IsAgentVisible: false })
        {
            if (_agentHost is not null)
                await _agentHost.OnChatNavigationCompletedAsync();
            ScheduleInjectBurst(forceReset: false);
        }
    }

    private async void OnAgentNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _agentHost is null) return;
        if (_webHost is { IsAgentVisible: true })
            await _agentHost.OnAgentNavigationCompletedAsync();
    }

    private async Task NavigateWebAsync(string url)
    {
        if (!_webViewReady || _webHost is null) return;

        if (AppNavigation.IsAgentPage(url))
        {
            await _webHost.SwitchToUrlAsync(url);
            await _webHost.WorkMode.ShowAgentSurfaceAsync();
            await _webHost.WorkMode.BroadcastImmediateAsync();
            _webHost.WorkMode.ScheduleBroadcastRetries();
            if (_agentHost is not null)
            {
                _ = _agentHost.SyncTokenFromChatPageAsync();
                _ = _agentHost.OnAgentNavigationCompletedAsync();
            }
            return;
        }

        await _webHost.SwitchToUrlAsync(url);
        await _webHost.WorkMode.ShowChatSurfaceAsync();
        await _webHost.WorkMode.BroadcastImmediateAsync();
        _webHost.WorkMode.ScheduleBroadcastRetries();
    }

    private void OnSpaNavigation(object? sender, object e)
    {
        if (_webHost is { IsAgentVisible: true })
            return;
        ScheduleInjectBurst(forceReset: false);
    }

    private void ScheduleInjectBurst(bool forceReset)
    {
        if (_webHost is null || !_webViewReady) return;

        _injectBurstCts?.Cancel();
        _injectBurstCts?.Dispose();
        _injectBurstCts = new CancellationTokenSource();
        var ct = _injectBurstCts.Token;
        var reset = forceReset;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, ct);
                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_webHost is not null && !ct.IsCancellationRequested)
                        await _webHost.BurstChatInjectAsync(ct, reset);
                });
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer navigation
            }
        }, ct);
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        var core = sender as CoreWebView2 ?? WebView.CoreWebView2;
        if (_webHost is { IsAgentVisible: true } && core != AgentWebView.CoreWebView2)
            return;
        if (_webHost is { IsAgentVisible: false } && core != WebView.CoreWebView2)
            return;

        var title = core?.DocumentTitle?.Trim();
        if (string.IsNullOrWhiteSpace(title))
            Title = "DeepSeek";
        else if (title.Contains("Agent", StringComparison.OrdinalIgnoreCase))
            Title = title;
        else if (title.StartsWith("DeepSeek", StringComparison.OrdinalIgnoreCase))
            Title = title;
        else
            Title = $"DeepSeek - {title}";
    }

    private void ScheduleWorkModeSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_agentHost is null || _webHost is null) return;
                    WorkModeTrace.Write("SelfTest: ApplyWorkMode agent");
                    await _agentHost.VerifyWorkModeSwitchAsync("agent");
                    await Task.Delay(1200);
                    WorkModeTrace.Write($"SelfTest: after agent IsAgentVisible={_webHost.IsAgentVisible}");

                    await _agentHost.VerifyWorkModeSwitchAsync("chat");
                    await Task.Delay(1200);
                    WorkModeTrace.Write($"SelfTest: after chat IsAgentVisible={_webHost.IsAgentVisible}");

                    var core = WebView.CoreWebView2;
                    if (core is not null)
                    {
                        await core.ExecuteScriptAsync(
                            "(function(){try{"
                            + "if(window.DsWorkMode&&window.DsWorkMode.requestToggle){window.DsWorkMode.requestToggle({});return;}"
                            + "if(window.chrome&&window.chrome.webview){"
                            + "window.chrome.webview.postMessage(JSON.stringify({type:'toggleWorkMode'}));}"
                            + "}catch(e){console.warn(e);}})();");
                        await Task.Delay(1500);
                        WorkModeTrace.Write($"SelfTest: after JS toggle IsAgentVisible={_webHost.IsAgentVisible}");
                    }
                });
                return;
            }

            WorkModeTrace.Write("SelfTest: timeout waiting for web ready");
        });
    }

    private void ScheduleAgentHelloSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null || _webHost is null) return;
                        WorkModeTrace.Write("AgentSelfTest: web ready, warming bridge");
                        await _agentHost.WarmChat2ApiBridgeAsync();
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        _agentHost.ReloadConfig();
                        var cfg = ConfigStore.Load();
                        if (string.IsNullOrWhiteSpace(cfg.WebUserToken))
                        {
                            WorkModeTrace.Write("AgentSelfTest: FAIL no webUserToken (login required)");
                            Environment.ExitCode = 2;
                            Application.Current.Shutdown();
                            return;
                        }

                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        await _agentHost.VerifyAgentHelloAsync(cts.Token);
                        Environment.ExitCode = 0;
                        Application.Current.Shutdown();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("AgentSelfTest: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("AgentSelfTest: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        });
    }

    private void ScheduleAgentTaskSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null || _webHost is null) return;
                        WorkModeTrace.Write("AgentTaskTest: web ready, warming bridge");
                        await _agentHost.WarmChat2ApiBridgeAsync();
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        _agentHost.ReloadConfig();
                        var cfg = ConfigStore.Load();
                        if (string.IsNullOrWhiteSpace(cfg.WebUserToken))
                        {
                            WorkModeTrace.Write("AgentTaskTest: FAIL no webUserToken (login required)");
                            Environment.ExitCode = 2;
                            Application.Current.Shutdown();
                            return;
                        }

                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                        await _agentHost.VerifyAgentTaskAsync(cts.Token);
                        Environment.ExitCode = 0;
                        Application.Current.Shutdown();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("AgentTaskTest: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("AgentTaskTest: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        });
    }

    private void ScheduleShutdownVerifyExit()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 90; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _agentHost is not null;
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null) return;
                        WorkModeTrace.Write("ShutdownVerify: warming embedded stack");
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        WorkModeTrace.Write("ShutdownVerify: exiting gracefully");
                        ExitApplication();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("ShutdownVerify: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("ShutdownVerify: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => Application.Current.Shutdown());
        });
    }

    private static void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private CoreWebView2? Core => _webViewReady ? WebView.CoreWebView2 : null;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control))
        {
            Core?.Reload();
            e.Handled = true;
        }
        else if (e.Key == Key.F12)
        {
            Core?.OpenDevToolsWindow();
            e.Handled = true;
        }
        else if (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (Core?.CanGoBack == true) Core.GoBack();
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (Core?.CanGoForward == true) Core.GoForward();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
