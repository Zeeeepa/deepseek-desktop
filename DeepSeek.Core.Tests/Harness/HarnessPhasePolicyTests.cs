using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessPhasePolicyTests
{
    [Fact]
    public void Verify_phase_disallows_tools()
    {
        Assert.False(HarnessPhasePolicy.AllowsTools(HarnessPhase.Verify, blueprintFinalized: false));
        Assert.True(HarnessPhasePolicy.IsReadonlyPhase(HarnessPhase.Verify));
        Assert.Equal("verify", HarnessPhasePolicy.TraceLabel(HarnessPhase.Verify));
    }

    [Fact]
    public void Blueprint_workflow_explore_allows_tools_until_finalized()
    {
        Assert.True(HarnessPhasePolicy.AllowsTools(HarnessPhase.Explore, blueprintFinalized: false));
        Assert.False(HarnessPhasePolicy.AllowsTools(HarnessPhase.Blueprint, blueprintFinalized: true));
    }
}
