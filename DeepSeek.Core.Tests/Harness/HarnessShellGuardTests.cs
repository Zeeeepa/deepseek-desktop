using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessShellGuardTests
{
    [Theory]
    [InlineData("format c:")]
    [InlineData("del /s /q *")]
    [InlineData("powershell -enc abc")]
    public void BlockReason_blocks_dangerous_commands(string command)
    {
        Assert.NotNull(HarnessShellGuard.BlockReason(command));
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("dir")]
    [InlineData("dotnet build")]
    public void BlockReason_allows_safe_commands(string command)
    {
        Assert.Null(HarnessShellGuard.BlockReason(command));
    }
}
