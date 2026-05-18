using System.Diagnostics;
using System.IO;
using System.Net.Http;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>托管 <c>deepseek serve --http</c>（<see href="https://github.com/Hmbown/DeepSeek-TUI/blob/main/docs/RUNTIME_API.md">Runtime API</see>）。</summary>
public sealed class DeepSeekTuiHost : IAsyncDisposable
{
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly object _gate = new();
    private Process? _process;
    private string? _baseUrl;
    private string? _runtimeBearerToken;

    public string BaseUrl => _baseUrl ?? $"http://127.0.0.1:{DefaultPort}";

    /// <summary>与 <c>DEEPSEEK_RUNTIME_TOKEN</c> / <c>--auth-token</c> 一致，供 Runtime API 客户端使用。</summary>
    public string? RuntimeBearerToken => _runtimeBearerToken;

    public const int DefaultPort = 7878;

    public async Task EnsureRunningAsync(AppConfig config, CancellationToken ct)
    {
        var port = config.DeepSeekTuiRuntimePort > 0 ? config.DeepSeekTuiRuntimePort : DefaultPort;
        _baseUrl = $"http://127.0.0.1:{port}";
        _runtimeBearerToken = DeepSeekTuiRuntimeAuth.Resolve(config);

        if (_process is { HasExited: false } &&
            await IsHealthyAsync(ct).ConfigureAwait(false) &&
            await CanAccessRuntimeApiAsync(ct).ConfigureAwait(false))
            return;

        await ShutdownListenersOnPortAsync(port, ct).ConfigureAwait(false);

        await DeepSeekTuiBundle.EnsureBinariesAsync(config, ct).ConfigureAwait(false);
        DeepSeekTuiConfigSync.Apply(config);

        var dispatcher = DeepSeekTuiBundle.ResolveDispatcher(config.DeepSeekTuiExecutablePath)
                       ?? throw new InvalidOperationException(
                           "未找到 DeepSeek-TUI。请重新运行 build.ps1 自动下载，或访问 https://deepseek-tui.com/zh/install 安装。");

        var toolsDir = DeepSeekTuiBundle.ResolveCompanionDirectory(dispatcher, config)
                       ?? DeepSeekTuiBundle.ToolsDirectory;
        var workspace = AgentWorkspace.ResolveRoot(config);
        Directory.CreateDirectory(workspace);

        var authArg = EscapeCliArg(_runtimeBearerToken);

        var psi = new ProcessStartInfo
        {
            FileName = dispatcher,
            Arguments =
                $"serve --http --host 127.0.0.1 --port {port} " +
                $"--auth-token {authArg} " +
                "--cors-origin https://ds-agent.local",
            WorkingDirectory = toolsDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        ApplyProviderEnv(psi, config, _runtimeBearerToken);

        var proc = Process.Start(psi)
                   ?? throw new InvalidOperationException("无法启动 deepseek serve --http");

        lock (_gate)
        {
            _process = proc;
        }

        for (var i = 0; i < 80; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (proc.HasExited)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"DeepSeek-TUI 进程已退出 (code {proc.ExitCode})。\n{Trim(err, 800)}\n\n请运行 deepseek doctor 或查看 https://deepseek-tui.com/zh/install");
            }

            if (await IsHealthyAsync(ct).ConfigureAwait(false) &&
                await CanAccessRuntimeApiAsync(ct).ConfigureAwait(false))
                return;

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"DeepSeek-TUI 运行时未在 {BaseUrl} 就绪（鉴权探测失败）。详见 https://deepseek-tui.com/zh/docs");
    }

    public static void ApplyProviderEnv(ProcessStartInfo psi, AppConfig config, string? runtimeBearerToken)
    {
        var port = config.LocalApiPort > 0 ? config.LocalApiPort : 5111;
        psi.Environment["DEEPSEEK_BASE_URL"] = $"http://127.0.0.1:{port}/v1";
        psi.Environment["DEEPSEEK_API_KEY"] = !string.IsNullOrWhiteSpace(config.WebUserToken)
            ? config.WebUserToken
            : (!string.IsNullOrWhiteSpace(config.DeepSeekApiKey) ? config.DeepSeekApiKey : "edge-local");
        psi.Environment["DEEPSEEK_MODEL"] = "deepseek-v4-pro";
        if (!string.IsNullOrWhiteSpace(runtimeBearerToken))
            psi.Environment["DEEPSEEK_RUNTIME_TOKEN"] = runtimeBearerToken.Trim();

        var home = DeepSeekTuiConfigSync.HomeDirectory;
        psi.Environment["DEEPSEEK_HOME"] = home;
    }

    private async Task ShutdownListenersOnPortAsync(int port, CancellationToken ct)
    {
        lock (_gate)
            TryStopLocked();

        await Task.Delay(200, ct).ConfigureAwait(false);

        if (!await IsHealthyAsync(ct).ConfigureAwait(false))
            return;

        TryKillListenersOnPort(port);
        await Task.Delay(400, ct).ConfigureAwait(false);
    }

    private static void TryKillListenersOnPort(int port)
    {
        try
        {
            var script =
                $"Get-NetTCPConnection -LocalPort {port} -State Listen -ErrorAction SilentlyContinue | " +
                "ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch
        {
            // ignore
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await HealthClient.GetAsync(BaseUrl.TrimEnd('/') + "/health", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CanAccessRuntimeApiAsync(CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl.TrimEnd('/') + "/v1/threads?limit=1");
            if (!string.IsNullOrWhiteSpace(_runtimeBearerToken))
            {
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _runtimeBearerToken);
                req.Headers.TryAddWithoutValidation("X-DeepSeek-Runtime-Token", _runtimeBearerToken);
            }

            using var resp = await HealthClient.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            TryStopLocked();
        }

        await Task.CompletedTask;
    }

    private void TryStopLocked()
    {
        try
        {
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string EscapeCliArg(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        return value.Contains('"') || value.Contains(' ')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }
}
