using DeepSeekBrowser.Services.Harness;
using Xunit;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessExecuteReplyGuardTests
{
    [Fact]
    public void IsProseOnlyTaskAnalysis_true_when_analysis_without_tools()
    {
        var answer =
            "【任务分析】\n类型：实现\n验收：可玩贪吃蛇\n计划：用 canvas 写单页。\n首先查看工作区，再创建文件。";
        Assert.True(HarnessExecuteReplyGuard.IsProseOnlyTaskAnalysis(answer, "写一个网页的贪吃蛇小游戏"));
    }

    [Fact]
    public void IsProseOnlyTaskAnalysis_false_when_tool_marker_present()
    {
        var answer = "【任务分析】\n<tool_calling><name>list_dir</name><arguments>{}</arguments></tool_calling>";
        Assert.False(HarnessExecuteReplyGuard.IsProseOnlyTaskAnalysis(answer, "写贪吃蛇"));
    }
}
