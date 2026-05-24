using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class WorkspacePathGuardTests
{
    [Fact]
    public void ResolveUnderWorkspace_relative_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsd-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var resolved = WorkspacePathGuard.ResolveUnderWorkspace(root, "sub\\file.txt");
            Assert.Equal(Path.Combine(root, "sub", "file.txt"), resolved);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveUnderWorkspace_rejects_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsd-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                WorkspacePathGuard.ResolveUnderWorkspace(root, "..\\outside.txt"));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveUnderWorkspace_rejects_empty()
    {
        var root = Path.Combine(Path.GetTempPath(), "dsd-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Throws<ArgumentException>(() =>
                WorkspacePathGuard.ResolveUnderWorkspace(root, "  "));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* ignore */ }
        }
    }
}
