using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessSandboxCoordinatorTests
{
    [Fact]
    public async Task BeginRunAsync_local_lazy_defers_acquire()
    {
        var state = new HarnessRunState { Phase = HarnessPhase.Execute };
        var config = new AppConfig { AgentSandboxLazyInit = true };

        await using var coord = await HarnessSandboxCoordinator.BeginRunAsync(
            state, config, Path.GetTempPath(), null, null, CancellationToken.None);

        Assert.Equal(HarnessSandboxKind.Local, coord.Kind);
        Assert.Null(coord.Current);
        Assert.False(string.IsNullOrWhiteSpace(state.SandboxSessionId));
    }

    [Fact]
    public async Task BeginRunAsync_uses_deterministic_session_from_run_id()
    {
        var state = new HarnessRunState { Phase = HarnessPhase.Execute, RunId = "run-stable-99" };
        var config = new AppConfig { AgentSandboxLazyInit = true };

        await using var coord = await HarnessSandboxCoordinator.BeginRunAsync(
            state, config, Path.GetTempPath(), null, null, CancellationToken.None);

        var expected = HarnessSandboxSessionIds.Deterministic("run:run-stable-99");
        Assert.Equal(expected, state.SandboxSessionId);
        Assert.Equal(expected, coord.SessionId);
    }

    [Fact]
    public async Task EnsureInitializedAsync_on_read_file_acquires_sandbox()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-sbx-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var probe = Path.Combine(workspace, "probe.txt");
        await File.WriteAllTextAsync(probe, "ok");

        var state = new HarnessRunState { Phase = HarnessPhase.Execute, RunId = "run-read-" + Guid.NewGuid().ToString("N") };
        var config = new AppConfig { AgentSandboxLazyInit = true };

        await using var coord = await HarnessSandboxCoordinator.BeginRunAsync(
            state, config, workspace, null, null, CancellationToken.None);

        Assert.Null(coord.Current);

        var executor = new HarnessToolExecutor(
            new McpHub(),
            new ApprovalGate(config, (_, _) => Task.FromResult(true)),
            new HarnessTrace());

        var json = """{"path":"probe.txt"}""";
        var result = await executor.ExecuteAsync(
            "read_file", json, config, workspace, HarnessPhase.Execute, CancellationToken.None, coord);

        Assert.Contains("ok", result);
        Assert.NotNull(coord.Current);

        try { Directory.Delete(workspace, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task EnsureInitializedAsync_local_runs_shell()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-sbx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var state = new HarnessRunState { Phase = HarnessPhase.Execute };
        var config = new AppConfig { AgentSandboxLazyInit = true };

        await using var coord = await HarnessSandboxCoordinator.BeginRunAsync(
            state, config, workspace, null, null, CancellationToken.None);

        var box = await coord.EnsureInitializedAsync(CancellationToken.None);
        var output = await box.ExecuteShellAsync("echo sandbox-ok", CancellationToken.None);

        Assert.Contains("sandbox-ok", output);
    }
}
