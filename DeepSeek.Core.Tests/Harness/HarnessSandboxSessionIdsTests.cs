using DeepSeekBrowser.Services.Harness;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessSandboxSessionIdsTests
{
    [Fact]
    public void Deterministic_same_key_same_id()
    {
        var a = HarnessSandboxSessionIds.Deterministic("run:test-1");
        var b = HarnessSandboxSessionIds.Deterministic("run:test-1");
        Assert.Equal(a, b);
        Assert.StartsWith("sbx-", a);
    }

    [Fact]
    public void ResolveStableKey_prefers_run_id()
    {
        var state = new HarnessRunState
        {
            RunId = "run-abc",
            WebChatSessionId = "web-xyz"
        };
        Assert.Equal("run:run-abc", HarnessSandboxSessionIds.ResolveStableKey(state));
    }
}
