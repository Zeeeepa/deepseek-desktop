using System.Text.Json;
using DeepSeekBrowser.Services.Harness;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeek.Core.Tests.Harness;

public sealed class BuiltinToolExecutorTests
{
    [Theory]
    [InlineData("read_file")]
    [InlineData("write_file")]
    [InlineData("list_dir")]
    [InlineData("grep")]
    [InlineData("glob")]
    [InlineData("run_shell")]
    public void IsBuiltin_recognizes_builtin_tools(string name)
    {
        Assert.True(BuiltinToolExecutor.IsBuiltin(name));
    }

    [Fact]
    public void IsBuiltin_rejects_unknown()
    {
        Assert.False(BuiltinToolExecutor.IsBuiltin("custom_tool"));
    }

    [Fact]
    public async Task WriteFile_then_read_file_round_trip_virtual_path()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-builtin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var executor = new BuiltinToolExecutor();
        var virt = HarnessVirtualPathMapper.WorkspaceVirtual + "/roundtrip.txt";

        try
        {
            var writeJson = JsonSerializer.Serialize(new { path = virt, content = "hello-sandbox" });
            var writeResult = await executor.ExecuteAsync(
                "write_file", writeJson, workspace, allowShell: false, CancellationToken.None);
            Assert.Contains("已写入", writeResult);

            var readJson = JsonSerializer.Serialize(new { path = virt });
            var readResult = await executor.ExecuteAsync(
                "read_file", readJson, workspace, allowShell: false, CancellationToken.None);
            Assert.Equal("hello-sandbox", readResult);
        }
        finally
        {
            try { Directory.Delete(workspace, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task ListDir_lists_workspace_via_virtual_dot()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dsd-builtin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "a.txt"), "x");
        var executor = new BuiltinToolExecutor();

        try
        {
            var json = JsonSerializer.Serialize(new { path = "." });
            var result = await executor.ExecuteAsync(
                "list_dir", json, workspace, allowShell: false, CancellationToken.None);
            Assert.Contains("a.txt", result);
        }
        finally
        {
            try { Directory.Delete(workspace, true); } catch { /* ignore */ }
        }
    }
}
