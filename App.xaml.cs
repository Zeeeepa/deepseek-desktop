using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DeepSeekBrowser;

public partial class App : System.Windows.Application
{
    private const string MutexName = "DeepSeekBrowser.SingleInstance";

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        var mutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        base.OnStartup(e);
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var proc in Process.GetProcessesByName(current.ProcessName))
            {
                if (proc.Id == current.Id) continue;
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
            }
        }
        catch
        {
            // ignore
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        Views.DsMessageDialog.Warning(
            null,
            $"DeepSeek 启动或运行出错：\n\n{e.Exception.Message}",
            "DeepSeek");
        e.Handled = true;
        Shutdown(-1);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogCrash(ex);
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekEdge",
                "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\r\n{ex}\r\n\r\n");
        }
        catch
        {
            // ignore
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
