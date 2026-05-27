using DeepSeekBrowser.Services.Harness;
using Xunit;

namespace DeepSeekBrowser.Tests.Harness;

public sealed class HarnessAnswerDisplayFilterTests
{
    [Fact]
    public void StripForDisplay_removes_tool_calling_and_json_tail()
    {
        var raw =
            "我将编写游戏。\n<tool_calling><name>write_file</name><arguments>{\"path\":\"/x.html\",\"content\":\"\n\n```html\n";
        var clean = HarnessAnswerDisplayFilter.StripForDisplay(raw);
        Assert.DoesNotContain("tool_calling", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```html", clean);
        Assert.Contains("我将编写游戏", clean);
    }

    [Fact]
    public void StripForDisplay_removes_empty_json_fence()
    {
        var raw = "【任务分析】计划如下\n```json\n```\n";
        var clean = HarnessAnswerDisplayFilter.StripForDisplay(raw);
        Assert.DoesNotContain("```json", clean);
        Assert.Contains("任务分析", clean);
    }

    [Fact]
    public void StripForDisplay_removes_loose_json_tool_object()
    {
        var raw =
            "我先查看目录。\n{ \"name\": \"list_dir\", \"arguments\": \"{\\\"path\\\":\\\"/mnt/user-data/workspace\\\"}\" }\n";
        var clean = HarnessAnswerDisplayFilter.StripForDisplay(raw);
        Assert.DoesNotContain("list_dir", clean);
        Assert.Contains("我先查看目录", clean);
    }
}
