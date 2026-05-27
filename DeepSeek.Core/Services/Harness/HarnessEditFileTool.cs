using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessEditFileTool
{
    public static HarnessEditResult Execute(JsonElement args, SandboxPathResolver paths, AppConfig? config = null)
    {
        var path = GetString(args, "file_path") ?? GetString(args, "path")
                   ?? throw new ArgumentException("edit 需要 file_path");
        var oldString = GetString(args, "old_string") ?? "";
        var newString = GetString(args, "new_string") ?? "";
        var replaceAll = args.TryGetProperty("replace_all", out var ra) && ra.ValueKind == JsonValueKind.True;

        string full;
        try
        {
            full = paths.ResolveWrite(path);
        }
        catch (Exception ex)
        {
            return HarnessEditResult.Error("ERROR: " + ex.Message);
        }

        if (!File.Exists(full))
            return HarnessEditResult.Error("ERROR: 文件不存在: " + path);

        var text = File.ReadAllText(full, Encoding.UTF8);
        var patch = HarnessPatchEngine.Apply(
            new HarnessPatchRequest
            {
                FilePath = path,
                OldString = oldString,
                NewString = newString,
                ReplaceAll = replaceAll
            },
            text);

        if (!patch.Success)
        {
            var msg = "ERROR: " + (patch.Error ?? "patch 失败");
            if (!string.IsNullOrWhiteSpace(patch.ClosestMatchHint))
                msg += "\n最接近的上下文:\n" + patch.ClosestMatchHint;
            return HarnessEditResult.Error(msg);
        }

        var preview = string.Equals(
            config?.AgentEditMode,
            "preview",
            StringComparison.OrdinalIgnoreCase);

        var virtualPath = paths.ToVirtual(full);
        var language = HarnessFilePreview.LanguageFromExtension(path);

        if (preview)
        {
            var patchId = HarnessPatchStaging.Stage(
                paths.Mapper.WorkspaceRoot,
                virtualPath,
                language,
                patch);
            return HarnessEditResult.FromPending(patchId, virtualPath, patch);
        }

        File.WriteAllText(full, patch.PatchedContent, Encoding.UTF8);
        return HarnessEditResult.Applied(virtualPath, patch);
    }

    public static string Execute(JsonElement args, SandboxPathResolver paths) =>
        Execute(args, paths, null).ToToolOutput();

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}

public sealed class HarnessEditResult
{
    public bool Ok { get; init; }
    public string Output { get; init; } = "";
    public string? PatchId { get; init; }
    public string? VirtualPath { get; init; }
    public HarnessPatchResult? Patch { get; init; }
    public AgentPendingPatch? Pending { get; init; }

    public string ToToolOutput() => Output;

    public static HarnessEditResult Error(string message) =>
        new() { Ok = false, Output = message };

    public static HarnessEditResult Applied(string virtualPath, HarnessPatchResult patch) =>
        new()
        {
            Ok = true,
            VirtualPath = virtualPath,
            Patch = patch,
            Output = "已编辑 " + virtualPath + " (" + patch.MatchCount + " 处替换)"
        };

    public static HarnessEditResult FromPending(string patchId, string virtualPath, HarnessPatchResult patch)
    {
        var pending = new AgentPendingPatch(
            patchId,
            virtualPath,
            HarnessFilePreview.LanguageFromExtension(virtualPath),
            patch.OriginalContent,
            patch.PatchedContent,
            patch.UnifiedDiff,
            AppliedToDisk: false);
        return new()
        {
            Ok = true,
            PatchId = patchId,
            VirtualPath = virtualPath,
            Patch = patch,
            Pending = pending,
            Output = "PATCH_PENDING:" + patchId + " 已生成 diff 预览，等待用户 Accept 后落盘 (" + virtualPath + ")"
        };
    }
}
