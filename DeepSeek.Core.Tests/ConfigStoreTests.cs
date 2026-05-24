using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeek.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "deepseek_desktop_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Environment.SetEnvironmentVariable(DeepSeekDesktopApp.ConfigDirEnvVar, _tempDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DeepSeekDesktopApp.ConfigDirEnvVar, null);
        Environment.SetEnvironmentVariable(DeepSeekDesktopApp.LegacyConfigDirEnvVar, null);
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void Load_returns_default_when_missing()
    {
        var cfg = ConfigStore.Load();
        Assert.Equal("deepseek-v4-pro", cfg.Model);
        Assert.Equal("chat", cfg.DefaultWorkMode);
        Assert.False(cfg.AgentDeepThinking);
        Assert.False(cfg.AgentWebSearch);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var cfg = new AppConfig
        {
            Model = "deepseek-v4-pro",
            DefaultWorkMode = "agent",
            WebUserToken = "test-token"
        };
        ConfigStore.Save(cfg);
        var loaded = ConfigStore.Load();
        Assert.Equal("deepseek-v4-pro", loaded.Model);
        Assert.Equal("agent", loaded.DefaultWorkMode);
        Assert.Equal("test-token", loaded.WebUserToken);
    }

    [Fact]
    public void Load_migrates_legacy_port_5111()
    {
        var json = """{"localApiPort":5111,"enableExternalOpenAiApi":false}""";
        File.WriteAllText(ConfigStore.ConfigFilePath, json);
        var loaded = ConfigStore.Load();
        Assert.Equal(0, loaded.LocalApiPort);
    }

    [Fact]
    public void Save_is_thread_safe_under_concurrent_writes()
    {
        var errors = 0;
        Parallel.For(0, 24, i =>
        {
            try
            {
                ConfigStore.Save(new AppConfig
                {
                    Model = "model-" + i,
                    DefaultWorkMode = i % 2 == 0 ? "chat" : "agent"
                });
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        });

        Assert.Equal(0, errors);
        Assert.True(File.Exists(ConfigStore.ConfigFilePath));
        var loaded = ConfigStore.Load();
        Assert.StartsWith("model-", loaded.Model);
    }
}
