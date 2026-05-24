using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessVirtualPathMapperTests
{
    [Fact]
    public void ResolveToPhysical_workspace_virtual_path()
    {
        var ws = Path.Combine(Path.GetTempPath(), "dsd-vpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            var mapper = new HarnessVirtualPathMapper(ws);
            var physical = mapper.ResolveToPhysical("/mnt/user-data/workspace/foo/bar.txt");
            var expected = Path.Combine(ws, "foo", "bar.txt");
            Assert.Equal(Path.GetFullPath(expected), physical);
        }
        finally
        {
            try { Directory.Delete(ws, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ResolveToVirtual_round_trip()
    {
        var ws = Path.Combine(Path.GetTempPath(), "dsd-vpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        var file = Path.Combine(ws, "a.txt");
        File.WriteAllText(file, "x");
        try
        {
            var mapper = new HarnessVirtualPathMapper(ws);
            var virt = mapper.ResolveToVirtual(file);
            Assert.StartsWith(HarnessVirtualPathMapper.WorkspaceVirtual, virt, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(file, mapper.ResolveToPhysical(virt));
        }
        finally
        {
            try { Directory.Delete(ws, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void IsReadOnlyTarget_blocks_uploads_write()
    {
        var ws = Path.Combine(Path.GetTempPath(), "dsd-vpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            var resolver = new SandboxPathResolver(ws);
            Assert.Throws<UnauthorizedAccessException>(() =>
                resolver.ResolveWrite("/mnt/user-data/uploads/secret.txt"));
        }
        finally
        {
            try { Directory.Delete(ws, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void VirtualizeText_replaces_workspace_root()
    {
        var ws = Path.Combine(Path.GetTempPath(), "dsd-vpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            var mapper = new HarnessVirtualPathMapper(ws);
            var text = mapper.VirtualizeText("file at " + ws + "\\sub\\x.txt");
            Assert.Contains(HarnessVirtualPathMapper.WorkspaceVirtual, text);
            Assert.DoesNotContain(ws, text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(ws, true); } catch { /* ignore */ }
        }
    }
}
