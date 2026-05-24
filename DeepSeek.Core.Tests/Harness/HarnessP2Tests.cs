using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessP2Tests
{
    [Fact]
    public void ToolOutputSpill_writes_large_output_to_disk()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var runId = "run-test123";
        var big = string.Join('\n', Enumerable.Range(1, 500).Select(i => "line-" + i));

        var summary = HarnessToolOutputSpill.Process("read_file", big, workspace, runId, 2000);

        Assert.Contains("已落盘", summary);
        Assert.Contains(".deepseek/runs/" + runId + "/observations/", summary.Replace('\\', '/'));
        Assert.True(summary.Length < big.Length);

        var spillDir = Path.Combine(workspace, ".deepseek", "runs", runId, "observations");
        Assert.True(Directory.Exists(spillDir));
        Assert.NotEmpty(Directory.GetFiles(spillDir, "read_file_*.txt"));
    }

    [Fact]
    public void ToolOutputSpill_keeps_small_output_inline()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-spill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        const string small = "ok";

        var result = HarnessToolOutputSpill.Process("echo", small, workspace, "run-x", 2000);

        Assert.Equal(small, result);
    }

    [Fact]
    public void VerifyChain_resolve_prefers_playbook_steps()
    {
        var playbook = new HarnessPlaybook
        {
            Verify = new HarnessPlaybookVerify
            {
                Steps =
                [
                    new HarnessVerifyStep { Command = "echo a", Name = "a" },
                    new HarnessVerifyStep { Command = "echo b", Name = "b", Optional = true }
                ]
            }
        };

        var steps = HarnessVerifyChain.Resolve(playbook, new AppConfig());

        Assert.Equal(2, steps.Count);
        Assert.Equal("a", steps[0].Name);
    }

    [Fact]
    public async Task VerifyChain_runs_steps_until_required_failure()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-vc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var steps = new[]
        {
            new HarnessVerifyStep { Command = "echo step1-ok", Name = "step1" },
            new HarnessVerifyStep { Command = "exit 1", Name = "fail", Optional = false },
            new HarnessVerifyStep { Command = "echo skipped", Name = "step3" }
        };

        var result = await HarnessVerifyChain.RunAsync(steps, workspace, CancellationToken.None);

        Assert.False(result.Passed);
        Assert.True(result.AnyRequiredFailed);
        Assert.Contains("step1-ok", result.CombinedOutput);
        Assert.DoesNotContain("skipped", result.CombinedOutput);
    }

    [Fact]
    public void ParseYaml_reads_verify_steps()
    {
        const string yaml = """
            id: chain-pb
            name: Chain
            strategy: execute
            verify:
              steps:
                - command: echo one
                  name: first
                  optional: true
                - command: echo two
                  name: second
                  timeout_seconds: 60
            """;

        var pb = HarnessPlaybookParser.ParseYaml(yaml);

        Assert.NotNull(pb.Verify);
        Assert.Equal(2, pb.Verify!.Steps.Count);
        Assert.Equal("echo one", pb.Verify.Steps[0].Command);
        Assert.True(pb.Verify.Steps[0].Optional);
        Assert.Equal("second", pb.Verify.Steps[1].Name);
        Assert.Equal(60, pb.Verify.Steps[1].TimeoutSeconds);
    }

    [Fact]
    public void RegistryReload_invalidates_playbook_cache()
    {
        HarnessRegistryReload.ReloadAll();
        var t = HarnessRegistryReload.LastReloadUtc;
        Assert.True(t > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void PostMortemWriter_creates_markdown_file()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-pm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var runId = "run-pm1";
        var state = new HarnessRunState { Phase = HarnessPhase.Execute, PlaybookId = "pb1" };

        HarnessPostMortemWriter.Write(
            workspace, runId,
            new HarnessDomainMatch { Id = "coding", Name = "Coding" },
            "do something", "done", state);

        var path = Path.Combine(workspace, ".deepseek", "runs", runId, "postmortem.md");
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("Post-Mortem", text);
        Assert.Contains("pb1", text);
    }
}
