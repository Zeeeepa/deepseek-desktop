using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness;
using Xunit;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessTokenOptimizationTests
{
    [Fact]
    public void AppConfig_token_defaults_are_conservative()
    {
        var config = new AppConfig();
        Assert.False(config.AgentIntentUseLlmPlanner);
        Assert.True(config.AgentPreferBuiltinOnExecute);
        Assert.True(config.AgentIntentCacheEnabled);
        Assert.True(config.AgentPromptMinimalMode);
        Assert.Equal(8, config.AgentMcpToolsMaxInRequest);
        Assert.Equal(16, config.AgentMcpCatalogMaxLines);
        Assert.Equal(3000, config.AgentToolOutputInlineMaxChars);
        Assert.Equal(3000, config.AgentSkillMaxChars);
        Assert.Equal(30, config.AgentWorkspaceSnapshotMaxEntries);
    }

    [Fact]
    public void ShouldSkipToolCatalog_when_minimal_and_intent_with_tools()
    {
        var config = new AppConfig { AgentPromptMinimalMode = true };
        var intent = new HarnessRunIntent
        {
            PlannedTools = [new HarnessPlannedTool { Name = "read_file", Purpose = "read", IsAvailable = true }]
        };
        Assert.True(HarnessPromptBudget.ShouldSkipToolCatalog(config, intent, includeXmlTools: true));
        Assert.False(HarnessPromptBudget.ShouldSkipToolCatalog(config, null, includeXmlTools: true));
        Assert.False(HarnessPromptBudget.ShouldSkipToolCatalog(config, intent, includeXmlTools: false));
    }

    [Fact]
    public void BuildPromptSection_minimal_limits_tools_and_omits_boilerplate()
    {
        var tools = Enumerable.Range(1, 6)
            .Select(i => new HarnessPlannedTool { Name = "tool" + i, Purpose = "p", IsAvailable = true })
            .ToList();
        var intent = new HarnessRunIntent { PlannedTools = tools, Analysis = "fix bug" };
        var section = intent.BuildPromptSection(includeToolHints: true, maxPlannedTools: 3, minimal: true);
        Assert.Contains("tool1", section);
        Assert.Contains("tool3", section);
        Assert.DoesNotContain("tool4", section);
        Assert.Contains("另有 3 个工具未列出", section);
        Assert.DoesNotContain("禁止编造", section);
    }

    [Fact]
    public void TrimMcpCatalog_respects_max_lines()
    {
        var catalog = string.Join('\n', Enumerable.Range(1, 40).Select(i => "- mcp__tool" + i));
        var trimmed = HarnessPromptBudget.TrimMcpCatalog(catalog, 8);
        Assert.Contains("已截断", trimmed);
    }
}
