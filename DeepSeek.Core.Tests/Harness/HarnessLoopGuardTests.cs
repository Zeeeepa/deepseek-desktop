using DeepSeekBrowser.Services.Harness;
using Xunit;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessLoopGuardTests
{
    [Fact]
    public void Blocks_identical_tool_calls_after_threshold()
    {
        var guard = new HarnessLoopGuard();
        Assert.True(guard.TryRecordAttempt("read_file", "{\"path\":\"a\"}", out _));
        Assert.True(guard.TryRecordAttempt("read_file", "{\"path\":\"a\"}", out _));
        Assert.False(guard.TryRecordAttempt("read_file", "{\"path\":\"a\"}", out var msg));
        Assert.Contains("已阻止", msg);
    }

    [Fact]
    public void Parallel_policy_allows_read_only_in_execute()
    {
        Assert.True(HarnessParallelToolPolicy.CanRunInParallel("read_file", HarnessPhase.Execute));
        Assert.False(HarnessParallelToolPolicy.CanRunInParallel("write_file", HarnessPhase.Execute));
    }
}
