using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>预览模式下的 patch 暂存；Accept 后落盘，Reject 丢弃。</summary>
public static class HarnessPatchStaging
{
    private static readonly ConcurrentDictionary<string, AgentPendingPatch> Pending = new();

    public static string Stage(
        string workspaceRoot,
        string virtualPath,
        string language,
        HarnessPatchResult patch)
    {
        var id = "patch_" + Guid.NewGuid().ToString("N")[..10];
        var pending = new AgentPendingPatch(
            id,
            virtualPath.Replace('\\', '/'),
            language,
            patch.OriginalContent,
            patch.PatchedContent,
            patch.UnifiedDiff,
            AppliedToDisk: false);
        Pending[id] = pending;
        Persist(workspaceRoot, pending);
        return id;
    }

    public static bool TryGet(string patchId, out AgentPendingPatch patch) =>
        Pending.TryGetValue(patchId, out patch);

    public static bool TryAccept(string workspaceRoot, string patchId, out string? error)
    {
        if (!Pending.TryRemove(patchId, out var patch))
        {
            error = "patch 不存在或已处理";
            return false;
        }

        try
        {
            var full = WorkspacePathGuard.ResolveUnderWorkspace(workspaceRoot, patch.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, patch.PatchedContent, Encoding.UTF8);
            DeleteMeta(workspaceRoot, patchId);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryReject(string workspaceRoot, string patchId)
    {
        if (!Pending.TryRemove(patchId, out _))
            return false;
        DeleteMeta(workspaceRoot, patchId);
        return true;
    }

    private static void Persist(string workspaceRoot, AgentPendingPatch patch)
    {
        try
        {
            var dir = MetaDir(workspaceRoot);
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(patch);
            File.WriteAllText(Path.Combine(dir, patch.PatchId + ".json"), json, Encoding.UTF8);
        }
        catch
        {
            // ignore disk persist failure
        }
    }

    private static void DeleteMeta(string workspaceRoot, string patchId)
    {
        try
        {
            var path = Path.Combine(MetaDir(workspaceRoot), patchId + ".json");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string MetaDir(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".deepseek", "patches");
}
