using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.ApiManagement;

namespace DeepSeek.Core.Tests;

public sealed class EmbeddedStackBridgeLinkerTests
{
    private sealed class MockBridge : IWebInjectBridge
    {
        public int SyncCalls { get; private set; }
        public int ReadyCalls { get; private set; }
        public int ProbeCalls { get; private set; }

        public Task SyncApiBridgeTokenAsync(string? webUserToken, CancellationToken ct = default)
        {
            SyncCalls++;
            return Task.CompletedTask;
        }

        public Task EnsureApiBridgeReadyAsync(CancellationToken ct = default)
        {
            ReadyCalls++;
            return Task.CompletedTask;
        }

        public Task<DsdApiHealth?> ProbeDsdApiHealthAsync(
            string? configWebUserToken,
            string baseUrl,
            CancellationToken ct = default)
        {
            ProbeCalls++;
            return Task.FromResult<DsdApiHealth?>(new DsdApiHealth
            {
                ApiListening = true,
                ConfigLoggedIn = true,
                BaseUrl = baseUrl
            });
        }
    }

    [Fact]
    public async Task LinkWebBridgeAsync_skips_without_token()
    {
        ProviderAccountStore.Save([]);
        var bridge = new MockBridge();
        var cfg = new AppConfig { WebUserToken = "" };
        await EmbeddedStackBridgeLinker.LinkWebBridgeAsync(cfg, bridge);
        Assert.Equal(0, bridge.SyncCalls);
    }

    [Fact]
    public async Task LinkWebBridgeAsync_syncs_when_token_present()
    {
        ProviderAccountStore.Save([
            new ProviderAccountRecord
            {
                Id = "acc-1",
                ProviderId = "deepseek",
                Status = "active",
                Credentials = new Dictionary<string, string> { ["token"] = "tok" }
            }
        ]);
        var bridge = new MockBridge();
        var cfg = new AppConfig();
        await EmbeddedStackBridgeLinker.LinkWebBridgeAsync(cfg, bridge);
        Assert.Equal(1, bridge.SyncCalls);
        Assert.Equal(1, bridge.ReadyCalls);
        Assert.Equal(1, bridge.ProbeCalls);
    }
}
