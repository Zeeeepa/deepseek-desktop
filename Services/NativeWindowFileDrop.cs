using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 通过 Win32 WM_DROPFILES 接收资源管理器拖放（WebView2/Chromium 子 HWND 也需注册）。
/// </summary>
public sealed class NativeWindowFileDrop : IDisposable
{
    private const int WmDropFiles = 0x0233;

    private readonly List<HwndSource> _sources = new();
    private readonly ConcurrentDictionary<IntPtr, HwndDropSubclass> _subclasses = new();
    private readonly HashSet<IntPtr> _registeredHwnds = new();
    private Func<bool>? _isActive;
    private Action<IReadOnlyList<string>>? _onPaths;
    private static NativeWindowFileDrop? _instance;

    public void Attach(
        Window window,
        WebView2? agentWebView,
        UIElement? dropSurface,
        Func<bool> isActive,
        Action<IReadOnlyList<string>> onPaths)
    {
        _instance = this;
        _isActive = isActive;
        _onPaths = onPaths;

        window.SourceInitialized += OnWindowSourceInitialized;
        if (window.IsLoaded)
            TryRegisterWindow(window);

        if (dropSurface is Visual gridVisual)
            TryRegisterVisual(gridVisual);

        if (agentWebView is null)
            return;

        agentWebView.AllowExternalDrop = false;
        agentWebView.Loaded += OnAgentWebViewLoaded;
        if (agentWebView.IsLoaded)
            RefreshAgentDropTargets(agentWebView, dropSurface);

        agentWebView.CoreWebView2InitializationCompleted += (_, _) =>
            RefreshAgentDropTargets(agentWebView, dropSurface);
    }

    public void RefreshAgentDropTargets(WebView2 agentWebView, UIElement? dropSurface = null)
    {
        if (dropSurface is Visual v)
            TryRegisterVisual(v);

        TryRegisterVisual(agentWebView);

        if (PresentationSource.FromVisual(agentWebView) is HwndSource root && root.Handle != IntPtr.Zero)
            RegisterHwndTree(root.Handle);
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is Window w)
            TryRegisterWindow(w);
    }

    private void OnAgentWebViewLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is WebView2 wv)
            RefreshAgentDropTargets(wv);
    }

    private void TryRegisterWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var src = HwndSource.FromHwnd(hwnd);
        if (src is not null)
            RegisterHwndSource(src);
    }

    public void TryRegisterVisual(Visual visual)
    {
        if (PresentationSource.FromVisual(visual) is not HwndSource src)
            return;
        RegisterHwndSource(src);
    }

    private void RegisterHwndSource(HwndSource src)
    {
        if (src.Handle == IntPtr.Zero)
            return;
        if (_sources.Any(s => s.Handle == src.Handle))
            return;

        DragAcceptFiles(src.Handle, true);
        src.AddHook(WndProc);
        _sources.Add(src);
        _registeredHwnds.Add(src.Handle);
    }

    private void RegisterHwndTree(IntPtr root)
    {
        RegisterHwndDropTarget(root);
        EnumChildWindows(
            root,
            (hwnd, _) =>
            {
                RegisterHwndDropTarget(hwnd);
                return true;
            },
            IntPtr.Zero);
    }

    private void RegisterHwndDropTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !_registeredHwnds.Add(hwnd))
            return;

        DragAcceptFiles(hwnd, true);

        var src = HwndSource.FromHwnd(hwnd);
        if (src is not null && _sources.All(s => s.Handle != hwnd))
        {
            src.AddHook(WndProc);
            _sources.Add(src);
            return;
        }

        _subclasses.GetOrAdd(hwnd, h => new HwndDropSubclass(h, HandleDropFiles));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        HandleDropMessage(msg, wParam, ref handled) ? IntPtr.Zero : IntPtr.Zero;

    private bool HandleDropMessage(int msg, IntPtr wParam, ref bool handled)
    {
        if (msg != WmDropFiles)
            return false;

        if (_isActive?.Invoke() != true)
        {
            DragFinish(wParam);
            handled = true;
            return true;
        }

        try
        {
            var paths = ReadDroppedPaths(wParam);
            if (paths.Count > 0)
            {
                System.Diagnostics.Trace.WriteLine("[DeepSeek] WM_DROPFILES: " + string.Join("; ", paths));
                _onPaths?.Invoke(paths);
            }

            handled = true;
        }
        finally
        {
            DragFinish(wParam);
        }

        return true;
    }

    private static void HandleDropFiles(IntPtr wParam)
    {
        if (_instance is null)
            return;
        var handled = false;
        _instance.HandleDropMessage(WmDropFiles, wParam, ref handled);
    }

    private static List<string> ReadDroppedPaths(IntPtr hDrop)
    {
        var list = new List<string>();
        var count = DragQueryFile(hDrop, 0xFFFFFFFF, null!, 0);
        var sb = new StringBuilder(2048);
        for (uint i = 0; i < count; i++)
        {
            sb.Clear();
            var len = DragQueryFile(hDrop, i, sb, sb.Capacity);
            if (len == 0)
                continue;
            var path = sb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(path))
                list.Add(path);
        }

        return list;
    }

    public void Dispose()
    {
        foreach (var sub in _subclasses.Values)
            sub.Dispose();
        _subclasses.Clear();

        foreach (var src in _sources)
        {
            if (src.Handle != IntPtr.Zero)
                DragAcceptFiles(src.Handle, false);
            src.RemoveHook(WndProc);
        }

        _sources.Clear();
        _registeredHwnds.Clear();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, int cch);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern bool DragFinish(IntPtr hDrop);

    private sealed class HwndDropSubclass : IDisposable
    {
        private static uint _nextId = 1;
        private readonly IntPtr _hwnd;
        private readonly SubclassProc _proc;
        private readonly UIntPtr _subclassId;
        private readonly GCHandle _procHandle;
        private bool _disposed;

        public HwndDropSubclass(IntPtr hwnd, Action<IntPtr> onDrop)
        {
            _hwnd = hwnd;
            _subclassId = new UIntPtr(_nextId++);
            _proc = (hWnd, msg, wParam, lParam, uId, refData) =>
            {
                if (msg == WmDropFiles)
                {
                    onDrop(wParam);
                    return IntPtr.Zero;
                }

                return DefSubclassProc(hWnd, msg, wParam, lParam);
            };
            _procHandle = GCHandle.Alloc(_proc);
            if (!SetWindowSubclass(_hwnd, _proc, _subclassId, IntPtr.Zero))
                throw new InvalidOperationException("SetWindowSubclass failed for hwnd " + _hwnd);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            RemoveWindowSubclass(_hwnd, _proc, _subclassId);
            _procHandle.Free();
        }

        private delegate IntPtr SubclassProc(
            IntPtr hWnd,
            uint uMsg,
            IntPtr wParam,
            IntPtr lParam,
            UIntPtr uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            UIntPtr uIdSubclass,
            IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(
            IntPtr hWnd,
            SubclassProc pfnSubclass,
            UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
    }
}
