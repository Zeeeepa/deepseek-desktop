using System.Diagnostics;
using System.Text;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

internal static class HarnessSandboxProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        CancellationToken ct,
        int timeoutMs = 300_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("无法启动: " + fileName);
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
        });

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var completed = await Task.WhenAny(proc.WaitForExitAsync(ct), Task.Delay(timeoutMs, ct));
        if (completed is not Task { IsCompletedSuccessfully: true } || !proc.HasExited)
        {
            try { proc.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
            throw new TimeoutException(fileName + " 超时");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var sb = new StringBuilder();
        sb.AppendLine("exit=" + proc.ExitCode);
        if (!string.IsNullOrWhiteSpace(stdout))
            sb.AppendLine(stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr))
            sb.AppendLine("stderr: " + stderr.TrimEnd());
        return (proc.ExitCode, sb.ToString().Trim());
    }
}
