using System.Text;
using DeepSeekBrowser.Services.Index;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessContextAssemblerOptions
{
    public int TopK { get; init; } = 5;
    public IReadOnlyList<string> RefPaths { get; init; } = Array.Empty<string>();
    public string? LspDiagnosticsBlock { get; init; }
}

/// <summary>漏斗式上下文拼装：rules → @files → LSP → hybrid search。</summary>
public static class HarnessContextAssembler
{
    public static async Task<string> AssembleAsync(
        string workspaceRoot,
        string userQuery,
        HarnessContextAssemblerOptions? options = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        options ??= new HarnessContextAssemblerOptions();
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return "";

        var sb = new StringBuilder();
        AppendRules(sb, workspaceRoot);
        AppendRefFiles(sb, workspaceRoot, options.RefPaths);
        if (!string.IsNullOrWhiteSpace(options.LspDiagnosticsBlock))
            sb.AppendLine(options.LspDiagnosticsBlock);

        try
        {
            await Task.Run(() =>
            {
                var indexer = CodebaseIndexer.GetOrCreate(workspaceRoot);
                indexer.EnsureWatching();
                var hits = indexer.HybridSearch(userQuery, options.TopK);
                if (hits.Count == 0)
                    return;
                sb.AppendLine("## 代码库检索（Top-" + hits.Count + "）");
                foreach (var h in hits)
                {
                    sb.AppendLine("### " + h.Path + " (score " + h.Score.ToString("F2") + ")");
                    sb.AppendLine("```");
                    sb.AppendLine(h.Snippet.TrimEnd());
                    sb.AppendLine("```");
                }
            }, ct);
        }
        catch
        {
            // 索引不可用时不阻断 Agent
        }

        return sb.Length == 0 ? "" : sb.ToString().Trim();
    }

    private static void AppendRules(StringBuilder sb, string workspaceRoot)
    {
        foreach (var name in new[] { ".cursorrules", ".deepseek/rules.md", "AGENTS.md" })
        {
            var path = Path.Combine(workspaceRoot, name);
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                if (text.Length > 4000)
                    text = text[..4000] + "\n…";
                sb.AppendLine("## 项目规则 (" + name + ")");
                sb.AppendLine(text.Trim());
                sb.AppendLine();
                return;
            }
            catch
            {
                // try next
            }
        }
    }

    private static void AppendRefFiles(StringBuilder sb, string workspaceRoot, IReadOnlyList<string> refPaths)
    {
        if (refPaths.Count == 0) return;
        sb.AppendLine("## 用户 @ 引用文件");
        foreach (var rel in refPaths.Take(8))
        {
            try
            {
                var full = WorkspacePathGuard.ResolveUnderWorkspace(workspaceRoot, rel);
                if (!File.Exists(full)) continue;
                var text = File.ReadAllText(full);
                if (text.Length > 8000)
                    text = text[..8000] + "\n…";
                sb.AppendLine("### " + rel.Replace('\\', '/'));
                sb.AppendLine("```");
                sb.AppendLine(text.TrimEnd());
                sb.AppendLine("```");
            }
            catch
            {
                // skip
            }
        }
    }
}
