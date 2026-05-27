using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Tests.Harness;

public class HarnessSecretScannerTests
{
    [Fact]
    public void DetectsOpenAiKeyPattern()
    {
        Assert.True(HarnessSecretScanner.ContainsSecret("key=sk-abcdefghijklmnopqrstuvwxyz123456"));
    }

    [Fact]
    public void CleanTextPasses()
    {
        Assert.False(HarnessSecretScanner.ContainsSecret("hello world"));
    }
}

public class WorkspacePathGuardTests
{
    [Fact]
    public void BlocksSensitivePath()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var sshDir = Path.Combine(root, ".ssh");
        Directory.CreateDirectory(sshDir);
        var keyFile = Path.Combine(sshDir, "id_rsa");
        File.WriteAllText(keyFile, "secret");
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                WorkspacePathGuard.ResolveUnderWorkspace(root, ".ssh/id_rsa"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
