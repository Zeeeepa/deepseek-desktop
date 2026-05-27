using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessXmlToolCallParserTests
{
    [Fact]
    public void TryParse_xml_block()
    {
        var text = "先看一下目录：\n<tool_calling>\n<name>list_dir</name>\n<arguments>{\"path\":\".\"}</arguments>\n</tool_calling>\n";
        var calls = HarnessXmlToolCallParser.TryParse(text, out var stripped);

        Assert.Single(calls);
        Assert.Equal("list_dir", calls[0].Name);
        Assert.Contains("\"path\"", calls[0].Arguments);
        Assert.DoesNotContain("tool_calling", stripped);
    }

    [Fact]
    public void TryParse_loose_list_directory_alias()
    {
        var text = "让我先确认目标文件夹是否存在:\n50129e5e__list_directory{\"path\": \"C:\\\\Users\\\\test\\\\folder\"}";
        var calls = HarnessXmlToolCallParser.TryParse(text, out var stripped);

        Assert.Single(calls);
        Assert.Equal("list_dir", calls[0].Name);
        Assert.DoesNotContain("list_directory", stripped);
    }

    [Theory]
    [InlineData("list_directory", "list_dir")]
    [InlineData("50129e5e__write", "write_file")]
    [InlineData("50129e5e__write_file", "write_file")]
    [InlineData("read", "read_file")]
    public void NormalizeToolName_maps_aliases(string input, string expected) =>
        Assert.Equal(expected, HarnessXmlToolCallParser.NormalizeToolName(input));

    [Fact]
    public void TryParse_call_line_with_mcp_prefix_and_html_fence()
    {
        var text =
            "【任务分析】\nCall: `82d65c5e__write_file`\n```html\n<!DOCTYPE html><html><body>game</body></html>\n```";
        var calls = HarnessXmlToolCallParser.TryParse(text, out _);

        Assert.Single(calls);
        Assert.Equal("write_file", calls[0].Name);
        Assert.Contains("index.html", calls[0].Arguments);
        Assert.Contains("game", calls[0].Arguments);
    }

    [Fact]
    public void HasToolCallMarkers_detects_call_line()
    {
        Assert.True(HarnessXmlToolCallParser.HasToolCallMarkers("Call: write_file\n"));
    }

    [Fact]
    public void TryParse_json_fence_list_dir()
    {
        var text = "计划如下：\n```json\n{\"name\":\"list_dir\",\"arguments\":{\"path\":\".\"}}\n```\n";
        var calls = HarnessXmlToolCallParser.TryParse(text, out var stripped);

        Assert.Single(calls);
        Assert.Equal("list_dir", calls[0].Name);
        Assert.DoesNotContain("```json", stripped);
    }

    [Fact]
    public void HasIncompleteToolArtifacts_detects_empty_json_fence()
    {
        Assert.True(HarnessXmlToolCallParser.HasIncompleteToolArtifacts("分析：\n```json\n```"));
    }

    [Fact]
    public void TryParse_loose_json_tool_object()
    {
        var text =
            "查看工作区：\n{ \"name\": \"list_dir\", \"arguments\": \"{\\\"path\\\":\\\"/mnt/user-data/workspace\\\"}\" }\n";
        var calls = HarnessXmlToolCallParser.TryParse(text, out var stripped);

        Assert.Single(calls);
        Assert.Equal("list_dir", calls[0].Name);
        Assert.Contains("path", calls[0].Arguments);
        Assert.DoesNotContain("\"name\"", stripped);
    }
}
