using DeepSeekBrowser.Services;
using Xunit;

namespace DeepSeek.Core.Tests.Config;

public sealed class ConfigFileRepairTests
{
    [Fact]
    public void WebUserTokenNormalizer_unwraps_null_wrapper()
    {
        var raw = "{\"value\":null,\"__version\":\"0\"}";
        Assert.Equal("", WebUserTokenNormalizer.Normalize(raw));
    }

    [Fact]
    public void WebUserTokenNormalizer_keeps_plain_token()
    {
        Assert.Equal("abc123", WebUserTokenNormalizer.Normalize("abc123"));
    }

    [Fact]
    public void PrepareJsonForDeserialize_repairs_unclosed_qwen_path()
    {
        var broken =
            "{\"qwenCodeWorkspaceRoot\":\"C:\\\\Users\\\\xiaow\\\\Desktop\\\\测试,\n" +
            "\"webUserToken\":\"x\"}";
        var fixedJson = ConfigFileRepair.PrepareJsonForDeserialize(broken, Path.GetTempPath());
        using var doc = System.Text.Json.JsonDocument.Parse(fixedJson);
        Assert.True(doc.RootElement.TryGetProperty("qwenCodeWorkspaceRoot", out var p));
        Assert.Contains("测试", p.GetString());
    }

    [Fact]
    public void TryRepairUserConfigFile_repairs_truncated_qwen_path_on_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dsd-config-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "config.json");
        var broken =
            "{\n  \"qwenCodeWorkspaceRoot\": \"C:\\\\Users\\\\xiaow\\\\Desktop\\\\新建文件夹,\n" +
            "  \"webUserToken\": \"tok\"\n}";
        File.WriteAllText(path, broken);

        var outcome = ConfigFileRepair.TryRepairUserConfigFile(dir);

        Assert.Equal(ConfigFileRepair.RepairOutcome.Repaired, outcome);
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("qwenCodeWorkspaceRoot", out var p));
        Assert.Contains("新建文件夹", p.GetString());
        try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
    }
}
