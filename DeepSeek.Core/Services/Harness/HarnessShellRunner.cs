using System.Diagnostics;
using System.Text;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>沙盒内 cmd 执行：超时、进程树终止、流式 stdout/stderr。</summary>
public static class HarnessShellRunner
{
    public const int MaxOutputChars = 30_000;
    private static readonly Dictionary<string, string> SessionCwds = new(StringComparer.Ordinal);

    public static async Task<string> RunAsync(
        string command,
        string workspaceKey,
        SandboxPathResolver paths,
        int timeoutMs,
        CancellationToken ct,
        Action<string>? onOutput = null)
    {
        var cwd = SessionCwds.GetValueOrDefault(workspaceKey) ?? paths.Mapper.WorkspaceRoot;
        TryApplyCdCommand(command, workspaceKey, cwd, paths);
        cwd = SessionCwds.GetValueOrDefault(workspaceKey) ?? paths.Mapper.WorkspaceRoot;

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 shell");
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var timedOut = false;
        var sw = Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Math.Max(timeoutMs, 1000));

        var stdoutTask = PumpAsync(proc.StandardOutput, stdout, onOutput, timeoutCts.Token);
        var stderrTask = PumpAsync(proc.StandardError, stderr, null, timeoutCts.Token);

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            HarnessProcessTree.Kill(proc.Id);
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }

        await Task.WhenAll(stdoutTask, stderrTask);

        var sb = new StringBuilder();
        if (timedOut)
            sb.AppendLine($"timedOut=true elapsedMs={sw.ElapsedMilliseconds}");
        sb.AppendLine("exit=" + (timedOut ? "-1" : proc.ExitCode.ToString()));
        if (stdout.Length > 0)
            sb.AppendLine(stdout.ToString().TrimEnd());
        if (stderr.Length > 0)
            sb.AppendLine("stderr: " + stderr.ToString().TrimEnd());

        var text = paths.VirtualizeText(sb.ToString().Trim());
        return text.Length > MaxOutputChars ? text[..MaxOutputChars] + "\n…(已截断)" : text;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        StringBuilder buffer,
        Action<string>? onOutput,
        CancellationToken ct)
    {
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                    break;
                buffer.AppendLine(line);
                onOutput?.Invoke(line + "\n");
            }
        }
        catch (OperationCanceledException)
        {
            // timeout or cancel
        }
    }

    internal static void TryApplyCdCommand(string command, string workspaceKey, string cwd, SandboxPathResolver paths)
    {
        var trimmed = command.Trim();
        if (!trimmed.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
            return;
        var arg = trimmed[3..].Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(arg))
            arg = paths.Mapper.WorkspaceRoot;
        try
        {
            var next = Path.GetFullPath(Path.IsPathRooted(arg) ? arg : Path.Combine(cwd, arg));
            if (Directory.Exists(next))
                SessionCwds[workspaceKey] = next;
        }
        catch
        {
            // ignore invalid cd
        }
    }
}
