using System.Text;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>L3 构建层：环境快照（参考 Meta-Harness / KTtao 任务流上下文装配）。</summary>
public static class HarnessWorkspaceBootstrap
{
    public static string BuildSnapshot(string workspaceRoot, int maxEntries = 80)
    {
        if (!Directory.Exists(workspaceRoot))
            return "工作区目录尚不存在，将在首次写入时创建。";

        var mapper = new HarnessVirtualPathMapper(workspaceRoot);
        var sb = new StringBuilder();
        sb.AppendLine("工作区快照（虚拟路径，根=" + HarnessVirtualPathMapper.WorkspaceVirtual + "）：");
        try
        {
            var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(workspaceRoot, "*", SearchOption.TopDirectoryOnly))
            {
                if (ShouldSkip(entry)) continue;
                var virt = mapper.ResolveToVirtual(entry);
                if (virt.Contains("/.git", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine((Directory.Exists(entry) ? "[dir]  " : "[file] ") + virt);
                count++;
                if (count >= maxEntries)
                {
                    sb.AppendLine("…(顶层目录已截断，可用 list_dir 继续探索子目录)");
                    break;
                }
            }

            if (count == 0)
                sb.AppendLine("(空目录)");
        }
        catch (Exception ex)
        {
            sb.AppendLine("(无法枚举: " + ex.Message + ")");
        }

        return sb.ToString().TrimEnd();
    }

    private static bool ShouldSkip(string path)
    {
        var name = Path.GetFileName(path);
        if (name is ".git" or "node_modules" or "bin" or "obj" or "publish") return true;
        if (name.StartsWith(".", StringComparison.Ordinal)) return true;
        return false;
    }
}
