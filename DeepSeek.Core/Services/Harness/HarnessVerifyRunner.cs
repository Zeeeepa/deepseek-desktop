using System.Diagnostics;
using System.Text;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessVerifyRunner
{
    public static async Task<HarnessVerifyResult> RunAsync(
        string command,
        string workspaceRoot,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new HarnessVerifyResult { Skipped = true, Output = "（未配置 Verify 命令）" };

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 Verify 命令");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 600)));

        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return new HarnessVerifyResult
            {
                ExitCode = -1,
                TimedOut = true,
                Output = "ERROR: Verify 超时 (" + timeoutSeconds + "s): " + command
            };
        }

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("command: " + command);
        sb.AppendLine("exit=" + proc.ExitCode);
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine("stderr: " + stderr.TrimEnd());

        var text = sb.ToString().Trim();
        if (text.Length > 80_000)
            text = text[..80_000] + "\n…(已截断)";

        return new HarnessVerifyResult
        {
            ExitCode = proc.ExitCode,
            Output = text,
            Passed = proc.ExitCode == 0
        };
    }
}

public sealed class HarnessVerifyResult
{
    public bool Skipped { get; init; }
    public bool TimedOut { get; init; }
    public int ExitCode { get; init; }
    public bool Passed { get; init; }
    public string Output { get; init; } = "";
}
