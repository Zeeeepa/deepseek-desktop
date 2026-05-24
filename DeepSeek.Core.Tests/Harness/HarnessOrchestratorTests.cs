using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessOrchestratorTests
{
    [Fact]
    public async Task RunAsync_blueprint_workflow_finalizes_without_approval()
    {
        var chat = new PlanModeFakeChat();
        var mcp = new McpHub();
        var approval = new ApprovalGate(new AppConfig { AgentApprovalMode = "always" }, (_, _) => Task.FromResult(false));
        var orchestrator = new HarnessOrchestrator(chat, mcp, approval);

        var request = new HarnessRunRequest
        {
            Config = new AppConfig { MaxAgentSteps = 8, WebUserToken = "tok" },
            Prompt = "分析此仓库结构",
            Strategy = AgentStrategies.Blueprint
        };

        var result = await orchestrator.RunAsync(request, new HarnessRunCallbacks(), CancellationToken.None);

        Assert.Contains("目标", result.Answer);
        Assert.True(chat.CompleteCalls >= 2);
    }

    private sealed class PlanModeFakeChat : IAgentWebChat
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
            if (CompleteCalls == 1 && allowToolCalls)
            {
                return Task.FromResult(new WebChatResult
                {
                    ToolCalls =
                    [
                        new WebToolCall { Id = "c1", Name = "list_dir", Arguments = """{"path":"."}""" }
                    ],
                    FinishReason = "tool_calls"
                });
            }

            return Task.FromResult(new WebChatResult
            {
                Content = "## 目标\n调研完成\n## 现状摘要\nok\n## 建议步骤\n1. 步骤\n## 风险与依赖\n无\n## 验收标准\n通过",
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

    [Fact]
    public async Task RunAsync_executes_tool_then_returns_final_answer()
    {
        var chat = new FakeAgentWebChat();
        var mcp = new McpHub();
        var approval = new ApprovalGate(new AppConfig { AgentApprovalMode = "never" }, (_, _) => Task.FromResult(true));
        var orchestrator = new HarnessOrchestrator(chat, mcp, approval);

        var request = new HarnessRunRequest
        {
            Config = new AppConfig { MaxAgentSteps = 5, WebUserToken = "tok" },
            Prompt = "read the README in the workspace",
            Strategy = AgentStrategies.Execute
        };

        var result = await orchestrator.RunAsync(request, new HarnessRunCallbacks(), CancellationToken.None);

        Assert.Equal("done", result.Answer);
        Assert.Equal(2, chat.CompleteCalls);
    }

    private sealed class FakeAgentWebChat : IAgentWebChat
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
                    ToolCalls =
                    [
                        new WebToolCall
                        {
                            Id = "c1",
                            Name = "read_file",
                            Arguments = """{"path":"readme.txt"}"""
                        }
                    ],
                    FinishReason = "tool_calls",
                    Model = model
                });
            }

            return Task.FromResult(new WebChatResult
            {
                Content = "done",
                FinishReason = "stop",
                Model = model
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
            var result = await CompleteAsync(
                messages, model, thinking, search, refFileIds, allowToolCalls, ct, webUserToken, webChatSessionId);
            yield return new WebChatStreamDone(result);
        }
    }
}
