using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessOrchestratorCasualTests
{
    [Fact]
    public async Task RunAsync_execute_hello_returns_natural_reply_not_blueprint()
    {
        var chat = new CasualFakeChat();
        var orchestrator = new HarnessOrchestrator(
            chat,
            new McpHub(),
            new ApprovalGate(new AppConfig { AgentApprovalMode = "never" }, (_, _) => Task.FromResult(true)));

        var result = await orchestrator.RunAsync(
            new HarnessRunRequest
            {
                Config = new AppConfig
                {
                    MaxAgentSteps = 5,
                    WebUserToken = "tok",
                },
                Prompt = "hello",
                Strategy = AgentStrategies.Execute
            },
            new HarnessRunCallbacks(),
            CancellationToken.None);

        Assert.Contains("Hi", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## 目标", result.Answer);
        Assert.Equal(1, chat.CompleteCalls);
        Assert.True(chat.LastAllowToolCalls);
    }

    [Fact]
    public async Task RunAsync_blueprint_hello_without_tools_returns_casual_not_forced_blueprint()
    {
        var chat = new CasualFakeChat();
        var orchestrator = new HarnessOrchestrator(
            chat,
            new McpHub(),
            new ApprovalGate(new AppConfig { AgentApprovalMode = "never" }, (_, _) => Task.FromResult(true)));

        var result = await orchestrator.RunAsync(
            new HarnessRunRequest
            {
                Config = new AppConfig
                {
                    MaxAgentSteps = 8,
                    WebUserToken = "tok",
                },
                Prompt = "hello",
                Strategy = AgentStrategies.Blueprint
            },
            new HarnessRunCallbacks(),
            CancellationToken.None);

        Assert.Contains("Hi", result.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("## 现状摘要", result.Answer);
    }

    private sealed class CasualFakeChat : IAgentWebChat
    {
        public int CompleteCalls;
        public bool LastAllowToolCalls = true;
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
            LastAllowToolCalls = allowToolCalls;
            return Task.FromResult(new WebChatResult
            {
                Content = "Hi! How can I help you today?",
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
