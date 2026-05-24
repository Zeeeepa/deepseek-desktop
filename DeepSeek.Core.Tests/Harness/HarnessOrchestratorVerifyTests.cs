using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessOrchestratorVerifyTests
{
    [Fact]
    public async Task RunAsync_execute_with_playbook_verify_runs_verify_phase()
    {
        var chat = new VerifyFakeChat();
        var mcp = new McpHub();
        var approval = new ApprovalGate(new AppConfig { AgentApprovalMode = "never" }, (_, _) => Task.FromResult(true));
        var orchestrator = new HarnessOrchestrator(chat, mcp, approval);

        var playbookDir = Path.Combine(Path.GetTempPath(), "dsd-pb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(playbookDir);
        var playbookId = "test-verify-" + Guid.NewGuid().ToString("N")[..8];
        var playbookPath = Path.Combine(playbookDir, playbookId + ".yaml");
        await File.WriteAllTextAsync(playbookPath, $$"""
            id: {{playbookId}}
            name: Test Verify
            strategy: execute
            verify:
              command: echo verify-ok
              optional: true
            """);

        var workspace = Path.Combine(Path.GetTempPath(), "dsd-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var homePlaybooks = Path.Combine(AgentDesktopConfigSync.HomeDirectory, "playbooks");
        Directory.CreateDirectory(homePlaybooks);
        File.Copy(playbookPath, Path.Combine(homePlaybooks, playbookId + ".yaml"), overwrite: true);
        HarnessPlaybookRegistry.InvalidateCache();

        var request = new HarnessRunRequest
        {
            Config = new AppConfig
            {
                MaxAgentSteps = 5,
                WebUserToken = "tok",
                AgentWorkspaceRoot = workspace,
            },
            Prompt = "完成任务",
            Strategy = AgentStrategies.Execute,
            PlaybookId = playbookId
        };

        var phases = new List<HarnessPhase>();
        var result = await orchestrator.RunAsync(
            request,
            new HarnessRunCallbacks { OnPhaseChanged = phases.Add },
            CancellationToken.None);

        Assert.Contains(HarnessPhase.Verify, phases);
        Assert.Contains("Verify", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.HarnessState);
        Assert.Contains("\"verifyCompleted\":true", result.HarnessState, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, chat.CompleteCalls);
    }

    private sealed class VerifyFakeChat : IAgentWebChat
    {
        public int CompleteCalls;

        public Task<WebChatResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            string model,
            bool thinking,
            bool search,
            IReadOnlyList<string> refFileIds,
            bool allowToolCalls,
            CancellationToken ct,
            string? webUserToken = null,
            string? webChatSessionId = null)
        {
            CompleteCalls++;
            if (CompleteCalls == 1)
            {
                return Task.FromResult(new WebChatResult
                {
                    Content = "execute done",
                    FinishReason = "stop"
                });
            }

            return Task.FromResult(new WebChatResult
            {
                Content = "## 结果\n执行完成，Verify 通过。",
                FinishReason = "stop"
            });
        }

        public async IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            string model,
            bool thinking,
            bool search,
            IReadOnlyList<string> refFileIds,
            bool allowToolCalls,
            CancellationToken ct,
            string? webUserToken = null,
            string? webChatSessionId = null)
        {
            yield return new WebChatStreamDone(await CompleteAsync(
                messages, model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId));
        }
    }
}
