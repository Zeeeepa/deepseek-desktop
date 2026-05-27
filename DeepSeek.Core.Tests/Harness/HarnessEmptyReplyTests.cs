using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessEmptyReplyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("(无回复)", true)]
    [InlineData("（无回复内容）", true)]
    [InlineData("正在创建 snake.html…", false)]
    public void IsEmpty_detects_placeholders(string? text, bool expected) =>
        Assert.Equal(expected, HarnessEmptyReply.IsEmpty(text));
}
