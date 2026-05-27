using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using Xunit;

namespace DeepSeek.Core.Tests.ApiManagement;

public sealed class AgentApiAccountIsolationTests : TestConfigIsolation
{
    [Fact]
    public void ResolveWebUserToken_does_not_fallback_to_config_by_default()
    {
        var config = new AppConfig { WebUserToken = "web-only-token" };

        var token = AccountCredentials.ResolveWebUserToken(null, config);

        Assert.Null(token);
    }

    [Fact]
    public void ResolveWebUserToken_reads_manual_account_credentials()
    {
        var config = new AppConfig { WebUserToken = "web-only-token" };
        var account = new ProviderAccountRecord
        {
            Id = "acc-test",
            ProviderId = "deepseek",
            Credentials = new Dictionary<string, string> { ["token"] = "manual-token" }
        };

        var token = AccountCredentials.ResolveWebUserToken(account, config);

        Assert.Equal("manual-token", token);
    }

    [Fact]
    public void ResolveFirstProviderWebToken_ignores_config_web_token()
    {
        ProviderAccountStore.Save([]);
        var config = new AppConfig { WebUserToken = "web-only-token" };

        var token = AccountCredentials.ResolveFirstProviderWebToken("deepseek", config);

        Assert.Null(token);
    }

    [Fact]
    public void ResolveWebUserTokenForRoute_uses_provider_account_when_route_account_null()
    {
        ProviderAccountStore.Save([
            new ProviderAccountRecord
            {
                Id = "acc-1",
                ProviderId = "deepseek",
                Status = "active",
                Credentials = new Dictionary<string, string> { ["token"] = "stored-token" }
            }
        ]);
        var config = new AppConfig();

        var token = AccountCredentials.ResolveWebUserTokenForRoute(null, config, "deepseek");

        Assert.Equal("stored-token", token);
    }

    [Fact]
    public void ResolveWebUserTokenForRoute_ignores_config_without_api_account()
    {
        ProviderAccountStore.Save([]);
        var config = new AppConfig { WebUserToken = "legacy-web-token" };

        var token = AccountCredentials.ResolveWebUserTokenForRoute(null, config, "deepseek");

        Assert.Null(token);
    }

    [Fact]
    public void SyncConfigWebTokenFromApiAccounts_copies_provider_token()
    {
        ProviderAccountStore.Save([
            new ProviderAccountRecord
            {
                Id = "acc-1",
                ProviderId = "deepseek",
                Status = "active",
                Credentials = new Dictionary<string, string> { ["token"] = "stored-token" }
            }
        ]);
        var config = new AppConfig { WebUserToken = "stale" };

        AccountCredentials.SyncConfigWebTokenFromApiAccounts(config);

        Assert.Equal("stored-token", config.WebUserToken);
    }
}
