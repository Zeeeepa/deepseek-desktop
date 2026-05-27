using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessRunIntentPlannerTests
{
    [Fact]
    public void Tool_inventory_parses_mcp_catalog_and_checks_availability()
    {
        var catalog =
            "已连接的 MCP 工具：\n" +
            "- github__search_code — search\n" +
            "- local__ping";
        var inv = HarnessToolInventory.Build(new AppConfig { EnableSubAgents = true }, catalog);

        Assert.True(inv.IsAvailable("read_file"));
        Assert.True(inv.IsAvailable("github__search_code"));
        Assert.False(inv.IsAvailable("nonexistent_tool"));
        Assert.Equal("read_file", inv.ResolveAvailableName("read"));
    }

    [Fact]
    public async Task Heuristic_plan_selects_exploration_tools_for_architecture_prompt()
    {
        var config = new AppConfig
        {
            AgentAutoIntentRouting = true,
            EnableSubAgents = true,
            EnableParallelExplore = true,
            AgentIntentUseLlmPlanner = false
        };
        var request = new HarnessRunRequest
        {
            Config = config,
            Prompt = "梳理 Harness 架构并列出 Orchestrator 相关文件",
            Strategy = AgentStrategies.Execute
        };

        var intent = await HarnessRunIntentPlanner.PlanAsync(
            request,
            new FakePlannerChat(),
            null,
            Path.GetTempPath(),
            CancellationToken.None);

        Assert.NotNull(intent);
        Assert.Contains(intent!.PlannedTools, t => t.Name is "parallel_explore" or "grep" or "list_dir");
    }

    [Fact]
    public void Should_plan_skips_casual_greeting()
    {
        var request = new HarnessRunRequest
        {
            Config = new AppConfig { AgentAutoIntentRouting = true },
            Prompt = "你好",
            Strategy = AgentStrategies.Execute
        };
        Assert.False(HarnessRunIntentPlanner.ShouldPlan(request, new HarnessRunState()));
    }

    [Fact]
    public void Should_use_llm_planner_when_enabled_and_non_casual()
    {
        var longPrompt =
            "梳理 Harness Orchestrator 架构并列出相关文件与调用链，说明各 Phase 如何切换、工具执行与 checkpoint 恢复流程，给出可验证的验收步骤。";
        Assert.True(HarnessRunIntentPlanner.ShouldUseLlmPlanner(new HarnessRunRequest
        {
            Config = new AppConfig { AgentIntentUseLlmPlanner = true },
            Prompt = longPrompt,
            Strategy = AgentStrategies.Execute
        }));

        Assert.False(HarnessRunIntentPlanner.ShouldUseLlmPlanner(new HarnessRunRequest
        {
            Config = new AppConfig { AgentIntentUseLlmPlanner = false },
            Prompt = longPrompt,
            Strategy = AgentStrategies.Execute
        }));

        Assert.False(HarnessRunIntentPlanner.ShouldUseLlmPlanner(new HarnessRunRequest
        {
            Config = new AppConfig { AgentIntentUseLlmPlanner = true },
            Prompt = "你好",
            Strategy = AgentStrategies.Execute
        }));

        Assert.True(HarnessRunIntentPlanner.ShouldUseLlmPlanner(new HarnessRunRequest
        {
            Config = new AppConfig { AgentIntentUseLlmPlanner = true },
            Prompt = "fix typo",
            Strategy = AgentStrategies.Execute
        }));
    }

    [Fact]
    public void Prompt_budget_trims_mcp_catalog()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 50).Select(i => $"- tool{i}__x — desc"));
        var trimmed = HarnessPromptBudget.TrimMcpCatalog("header\n" + lines, 10);
        Assert.Contains("截断", trimmed);
        Assert.DoesNotContain("tool50__x", trimmed);
    }

    private sealed class FakePlannerChat : IAgentWebChat
    {
        public Task<WebChatResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            string model,
            bool thinking,
            bool search,
            IReadOnlyList<string> refFileIds,
            bool allowToolCalls,
            CancellationToken ct,
            string? webUserToken = null,
            string? webChatSessionId = null,
            AgentChatOptions? options = null) =>
            Task.FromResult(new WebChatResult
            {
                Content =
                    "{\"analysis\":\"调研任务\",\"skill_id\":null,\"tools\":[{\"name\":\"grep\",\"purpose\":\"查找文件\"}],\"notes\":\"只读优先\"}"
            });

        public IAsyncEnumerable<WebChatStreamEvent> StreamAsync(
            IReadOnlyList<ChatMessage> messages,
            string model,
            bool thinking,
            bool search,
            IReadOnlyList<string> refFileIds,
            bool allowToolCalls,
            CancellationToken ct,
            string? webUserToken = null,
            string? webChatSessionId = null,
            AgentChatOptions? options = null) =>
            throw new NotSupportedException();
    }
}
