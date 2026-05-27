using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
    private NativeWindowFileDrop? _nativeFileDrop;
    private static readonly Color WebViewBackground = Color.FromArgb(255, 255, 255, 255);

    public MainWindow()
    {
        InitializeComponent();
        _tray = new TrayIconService(this, ExitApplication);
        WireAgentFileDragRouting();
    }

    internal TrayIconService? GetTrayService() => _tray;

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
        App.RegisterMainWindowActivation(this);
        ApplyWindowIconInternal();

        try
        {
            Directory.CreateDirectory(UserDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder);

            var composition = new DesktopCompositionRoot(
                WebView,
                AgentWebView,
                BridgeWebView,
                Dispatcher,
                OnWorkModeSurfaceChanged,
                () => this);

            WebView.DefaultBackgroundColor = WebViewBackground;
            AgentWebView.DefaultBackgroundColor = WebViewBackground;

            await composition.InitializeAsync(env, NavigateWebAsync);

            _apiBridgeHost = composition.ApiBridgeHost;
            _webHost = composition.WebHost;
            _localApi = composition.LocalApi;
            _agentHost = composition.AgentHost;

            composition.AttachMainWindowHandlers(
                OnChatNavigationStarting,
                OnChatNavigationCompleted,
                OnAgentNavigationCompleted,
                OnSpaHistoryChanged,
                OnSpaSourceChanged,
                OnDocumentTitleChanged);

            // 拖放：WM_DROPFILES 覆盖 WebView2 区域；Grid 拖放作备用。
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    composition.AttachAgentDragDrop(WebSurfaceGrid);
                }
                catch (Exception dragEx)
                {
                    System.Diagnostics.Trace.WriteLine("[DeepSeek] Agent grid drag-drop disabled: " + dragEx);
                }

                try
                {
                    _nativeFileDrop?.Dispose();
                    _nativeFileDrop = new NativeWindowFileDrop();
                    AgentWebView.AllowExternalDrop = false;
                    WebSurfaceGrid.AllowDrop = true;
                    _nativeFileDrop.Attach(
                        this,
                        AgentWebView,
                        WebSurfaceGrid,
                        () => _webViewReady && _webHost is { IsAgentVisible: true },
                        paths =>
                        {
                            Dispatcher.BeginInvoke(() => DeliverAgentDroppedPaths(paths));
                        });
                    RefreshAgentNativeDropTargets();
                }
                catch (Exception dragEx)
                {
                    System.Diagnostics.Trace.WriteLine("[DeepSeek] WM_DROPFILES disabled: " + dragEx);
                }
            }, DispatcherPriority.Loaded);

            _webViewReady = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            OnWorkModeSurfaceChanged();
            ScheduleStartupVerificationIfNeeded();
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            var detail = ex.ToString();
            if (detail.Length > 1200)
                detail = detail[..1200] + "…";
            Views.DsMessageDialog.Warning(
                this,
                $"无法初始化 Edge 内核 (WebView2)。\n\n{ex.Message}\n\n{detail}\n\n请安装 Microsoft Edge WebView2 运行时：\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n请从 DDpublish\\DeepSeek.exe 启动最新版本。",
                "DeepSeek");
            _isExiting = true;
            Close();
        }
    }

    private void ScheduleStartupVerificationIfNeeded()
    {
        if (DeepSeekDesktopApp.IsEnvEnabled(
                DeepSeekDesktopApp.VerifyWorkModeEnvVar,
                DeepSeekDesktopApp.LegacyVerifyWorkModeEnvVar)
            || DeepSeekDesktopApp.IsEnvEnabled(
                DeepSeekDesktopApp.VerifySmoothnessEnvVar,
                DeepSeekDesktopApp.VerifySmoothnessEnvVar))
        {
            DesktopUiTrace.ResetCounters();
            ScheduleWorkModeSelfTest();
            return;
        }

        if (DeepSeekDesktopApp.IsEnvEnabled(
                DeepSeekDesktopApp.VerifyAgentTaskEnvVar,
                DeepSeekDesktopApp.VerifyAgentTaskEnvVar))
        {
            ScheduleAgentTaskSelfTest();
            return;
        }

        if (DeepSeekDesktopApp.IsEnvEnabled(
                DeepSeekDesktopApp.VerifyAgentEnvVar,
                DeepSeekDesktopApp.LegacyVerifyAgentEnvVar))
        {
            ScheduleAgentHelloSelfTest();
            return;
        }

        if (DeepSeekDesktopApp.IsEnvEnabled(
                DeepSeekDesktopApp.VerifyShutdownEnvVar,
                DeepSeekDesktopApp.VerifyShutdownEnvVar))
            ScheduleShutdownVerifyExit();
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

        _webHost?.CancelChatInject();

        _nativeFileDrop?.Dispose();
        _nativeFileDrop = null;

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
