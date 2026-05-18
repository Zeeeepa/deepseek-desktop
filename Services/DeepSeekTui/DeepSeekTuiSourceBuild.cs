using System.Diagnostics;
using System.IO;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>
/// 从本地 DeepSeek-TUI 源码树构建并解析二进制（可改 <c>DeepSeek-TUI-main</c> 后重新 build）。
/// 架构见 <see href="https://github.com/Hmbown/DeepSeek-TUI/blob/main/docs/ARCHITECTURE.md"/>。
/// </summary>
public static class DeepSeekTuiSourceBuild
{
    public static readonly string DefaultSourceCandidate =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "DSD",
            "DeepSeek-TUI-main",
            "DeepSeek-TUI-main");

    public static string? ResolveRepositoryRoot(AppConfig config)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.DeepSeekTuiSourcePath))
            candidates.Add(config.DeepSeekTuiSourcePath.Trim());

        candidates.Add(DefaultSourceCandidate);
        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "DSD",
            "DeepSeek-TUI-main"));

        foreach (var c in candidates)
        {
            var root = NormalizeRepoRoot(c);
            if (root is not null)
                return root;
        }

        return null;
    }

    public static (string Dispatcher, string Runtime)? TryResolveReleaseBinaries(AppConfig config)
    {
        var root = ResolveRepositoryRoot(config);
        if (root is null)
            return null;

        var dispatcher = Path.Combine(root, "target", "release", "deepseek.exe");
        var runtime = Path.Combine(root, "target", "release", "deepseek-tui.exe");
        if (File.Exists(dispatcher) && File.Exists(runtime))
            return (dispatcher, runtime);

        return null;
    }

    public static async Task<bool> TryCopyReleaseToToolsAsync(AppConfig config, CancellationToken ct = default)
    {
        var pair = TryResolveReleaseBinaries(config);
        if (pair is null)
            return false;

        Directory.CreateDirectory(DeepSeekTuiBundle.ToolsDirectory);
        File.Copy(pair.Value.Dispatcher, DeepSeekTuiBundle.DispatcherPath, overwrite: true);
        File.Copy(pair.Value.Runtime, DeepSeekTuiBundle.RuntimePath, overwrite: true);

        var version = await DeepSeekTuiBundle.TryGetVersionAsync(pair.Value.Dispatcher, ct).ConfigureAwait(false)
                      ?? DeepSeekTuiBundle.BundledVersion;
        await File.WriteAllTextAsync(
            Path.Combine(DeepSeekTuiBundle.ToolsDirectory, "version.txt"),
            version + " (local source)" + Environment.NewLine,
            ct).ConfigureAwait(false);
        return true;
    }

    public static async Task BuildReleaseAsync(string repositoryRoot, CancellationToken ct = default)
    {
        var cargo = FindCargo();
        if (cargo is null)
            throw new InvalidOperationException("未找到 cargo。请安装 Rust 1.88+：https://rustup.rs");

        var psi = new ProcessStartInfo
        {
            FileName = cargo,
            Arguments = "build --release -p deepseek-tui-cli -p deepseek-tui",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var proc = Process.Start(psi)
                       ?? throw new InvalidOperationException("无法启动 cargo build");

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "cargo build 失败 (exit " + proc.ExitCode + ").\n" +
                Trim(stderr, 2000) + "\n" + Trim(stdout, 800));
        }
    }

    private static string? NormalizeRepoRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var full = Path.GetFullPath(path.Trim());
        if (File.Exists(Path.Combine(full, "Cargo.toml")) &&
            Directory.Exists(Path.Combine(full, "crates", "tui")))
            return full;

        var nested = Path.Combine(full, "DeepSeek-TUI-main");
        if (File.Exists(Path.Combine(nested, "Cargo.toml")) &&
            Directory.Exists(Path.Combine(nested, "crates", "tui")))
            return nested;

        return null;
    }

    private static string? FindCargo()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var cargo = Path.Combine(dir.Trim(), "cargo.exe");
                if (File.Exists(cargo))
                    return cargo;
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
