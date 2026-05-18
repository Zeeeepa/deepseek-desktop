using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>
/// 捆绑并维护 DeepSeek-TUI 官方二进制（dispatcher + TUI runtime，须同目录）。
/// 见 <see href="https://deepseek-tui.com/zh/install">官方安装文档</see>。
/// </summary>
public static class DeepSeekTuiBundle
{
    /// <summary>与 npm 包 / GitHub Release 对齐（<see href="https://deepseek-tui.com/zh"/>）。</summary>
    public const string BundledVersion = "0.8.39";

    private const string ReleaseTag = "v0.8.39";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static string ToolsDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "tools");

    public static string DispatcherPath => Path.Combine(ToolsDirectory, "deepseek.exe");

    public static string RuntimePath => Path.Combine(ToolsDirectory, "deepseek-tui.exe");

    public static bool IsBundledComplete =>
        File.Exists(DispatcherPath) && File.Exists(RuntimePath);

    public static string? ResolveDispatcher(string? configuredPath, AppConfig? config = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        config ??= ConfigStore.Load();
        var fromSource = DeepSeekTuiSourceBuild.TryResolveReleaseBinaries(config);
        if (fromSource is not null)
            return fromSource.Value.Dispatcher;

        if (IsBundledComplete)
            return DispatcherPath;

        var npmDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "deepseek-tui", "bin", "downloads");
        var npmDispatcher = Path.Combine(npmDir, "deepseek.exe");
        if (File.Exists(npmDispatcher))
            return npmDispatcher;

        var npmCmd = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "deepseek.cmd");
        if (File.Exists(npmCmd))
            return npmCmd;

        return FindOnPath("deepseek.exe") ?? FindOnPath("deepseek.cmd");
    }

    public static string? ResolveCompanionDirectory(string dispatcherPath, AppConfig? config = null)
    {
        var dir = Path.GetDirectoryName(dispatcherPath);
        if (string.IsNullOrEmpty(dir))
            return ToolsDirectory;

        if (File.Exists(Path.Combine(dir, "deepseek-tui.exe")))
            return dir;

        config ??= ConfigStore.Load();
        var fromSource = DeepSeekTuiSourceBuild.TryResolveReleaseBinaries(config);
        if (fromSource is not null &&
            string.Equals(dispatcherPath, fromSource.Value.Dispatcher, StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(fromSource.Value.Runtime);

        if (IsBundledComplete)
            return ToolsDirectory;

        var npmDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "deepseek-tui", "bin", "downloads");
        if (File.Exists(Path.Combine(npmDir, "deepseek-tui.exe")))
            return npmDir;

        return dir;
    }

    public static async Task EnsureBinariesAsync(AppConfig? config = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(ToolsDirectory);
        config ??= ConfigStore.Load();

        if (await DeepSeekTuiSourceBuild.TryCopyReleaseToToolsAsync(config, ct).ConfigureAwait(false))
            return;

        if (IsBundledComplete)
            return;

        var baseUrl = $"https://github.com/Hmbown/DeepSeek-TUI/releases/download/{ReleaseTag}";
        await DownloadAsync($"{baseUrl}/deepseek-windows-x64.exe", DispatcherPath, ct).ConfigureAwait(false);
        await DownloadAsync($"{baseUrl}/deepseek-tui-windows-x64.exe", RuntimePath, ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(ToolsDirectory, "version.txt"),
            BundledVersion + Environment.NewLine,
            ct).ConfigureAwait(false);
    }

    public static async Task<string?> TryGetVersionAsync(string? dispatcherPath, CancellationToken ct = default)
    {
        var exe = ResolveDispatcher(dispatcherPath);
        if (string.IsNullOrEmpty(exe))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--version",
                WorkingDirectory = ResolveCompanionDirectory(exe) ?? ToolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return null;
            var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<JsonDocument?> TryDoctorJsonAsync(string? dispatcherPath, CancellationToken ct = default)
    {
        var exe = ResolveDispatcher(dispatcherPath);
        if (string.IsNullOrEmpty(exe))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "doctor --json",
                WorkingDirectory = ResolveCompanionDirectory(exe) ?? ToolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return null;
            var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return null;
            return JsonDocument.Parse(output);
        }
        catch
        {
            return null;
        }
    }

    private static async Task DownloadAsync(string url, string targetPath, CancellationToken ct)
    {
        var tmp = targetPath + ".download";
        await using (var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false))
        await using (var file = File.Create(tmp))
            await stream.CopyToAsync(file, ct).ConfigureAwait(false);

        if (File.Exists(targetPath))
            File.Delete(targetPath);
        File.Move(tmp, targetPath);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
