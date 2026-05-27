using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using Xunit;

namespace DeepSeek.Core.Tests.ApiManagement;

public sealed class AccountLoadBalancerDeepSeekModelTests : TestConfigIsolation
{
    [Fact]
    public void SelectAccount_includes_deepseek_account_for_v4_flash()
    {
        AccountLoadBalancer.Instance.ResetStateForTests();
        ProviderAccountStore.Save([
            new ProviderAccountRecord
            {
                Id = "acc-flash",
                ProviderId = "deepseek",
                Status = "active",
                Credentials = new Dictionary<string, string> { ["token"] = "tok" }
            }
        ]);

        var config = new AppConfig();
        ApiProviderRegistry.AddOrUpdate(config, new ApiProviderEntry
        {
            Id = "deepseek",
            DisplayName = "DeepSeek",
            Kind = ApiProviderKinds.BuiltinWeb,
            RouteMode = ApiRouteModes.EmbeddedWeb,
            Enabled = true,
            Models = ["deepseek-v4-pro"]
        });

        var sel = AccountLoadBalancer.Instance.SelectAccount(config, "deepseek-v4-flash", "deepseek");

        Assert.NotNull(sel);
        Assert.Equal("acc-flash", sel!.Account.Id);
    }
}
