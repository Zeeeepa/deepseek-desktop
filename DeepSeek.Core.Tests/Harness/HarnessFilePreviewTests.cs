using DeepSeekBrowser.Services.Harness;
using Xunit;

namespace DeepSeekBrowser.Tests.Harness;

public sealed class HarnessFilePreviewTests
{
    [Fact]
    public void TryBuildAfterTool_write_file_returns_preview()
    {
        var ws = Path.GetTempPath();
        var args = """{"file_path":"index.html","content":"<html><body>hi</body></html>"}""";
        var ok = HarnessFilePreview.TryBuildAfterTool("write_file", args, ws, true, out var preview);

        Assert.True(ok);
        Assert.Equal("index.html", preview.Path);
        Assert.Equal("html", preview.Language);
        Assert.Contains("hi", preview.Content);
    }

    [Fact]
    public void TryBuildAfterTool_skips_on_error()
    {
        var ok = HarnessFilePreview.TryBuildAfterTool(
            "write_file", """{"file_path":"a.txt","content":"x"}""", Path.GetTempPath(), false, out _);

        Assert.False(ok);
    }

    [Fact]
    public void NormalizeWriteFileArguments_repairs_fence_inside_broken_json()
    {
        var args =
            "{\"path\":\"snake_game.html\",\"content\":\"\\n```html\\n<!DOCTYPE html><html></html>\\n```\"}";
        var norm = HarnessXmlToolCallParser.NormalizeWriteFileArguments("write_file", args);
        Assert.Contains("file_path", norm);
        Assert.Contains("<!DOCTYPE html>", norm);
    }
}
