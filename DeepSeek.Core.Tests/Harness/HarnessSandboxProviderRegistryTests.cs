using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessSandboxProviderRegistryTests
{
    [Fact]
    public void Get_returns_local_provider_singleton()
    {
        HarnessSandboxProviderRegistry.Reset();
        try
        {
            var a = HarnessSandboxProviderRegistry.Get();
            var b = HarnessSandboxProviderRegistry.Get();
            Assert.Same(a, b);
            Assert.IsType<LocalWorkspaceSandboxProvider>(a);
        }
        finally
        {
            HarnessSandboxProviderRegistry.Reset();
        }
    }

    [Fact]
    public void Shutdown_clears_provider()
    {
        HarnessSandboxProviderRegistry.Reset();
        _ = HarnessSandboxProviderRegistry.Get();
        HarnessSandboxProviderRegistry.Shutdown();
        var again = HarnessSandboxProviderRegistry.Get();
        Assert.IsType<LocalWorkspaceSandboxProvider>(again);
    }
}
