using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

public static class HarnessSandboxFactory
{
    public static Task<(IHarnessSandboxProvider Provider, HarnessSandboxKind EffectiveKind)>
        CreateProviderAsync(AppConfig config, CancellationToken ct)
    {
        _ = ct;
        var provider = HarnessSandboxProviderRegistry.Get(config);
        return Task.FromResult<(IHarnessSandboxProvider, HarnessSandboxKind)>(
            (provider, provider.Kind));
    }
}
