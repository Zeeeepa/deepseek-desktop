using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeek.Core.Tests.Config;

public sealed class ConfigSchemaMigrationTests
{
    [Fact]
    public void MigrateAndRepair_v1_enables_agent_thinking_defaults()
    {
        var cfg = new AppConfig
        {
            ConfigSchemaVersion = 0,
            AgentDeepThinking = false,
            AgentWebSearch = false
        };

        var result = ConfigFileRepair.MigrateAndRepair(cfg, Path.GetTempPath(), out var changed);

        Assert.True(result);
        Assert.True(changed);
        Assert.True(cfg.AgentDeepThinking);
        Assert.True(cfg.AgentWebSearch);
        Assert.Equal(2, cfg.ConfigSchemaVersion);
    }
}
