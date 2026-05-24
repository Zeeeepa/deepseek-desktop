using System.Text;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 工具大输出落盘 + 摘要注入（P2：避免 Observation 撑爆 Model 上下文）。
/// </summary>
public static class HarnessToolOutputSpill
{
    public static string Process(
        string toolName,
        string result,
        string workspaceRoot,
        string runId,
        int inlineMaxChars)
    {
        if (string.IsNullOrEmpty(result) || result.Length <= inlineMaxChars)
            return result;

        if (result.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            return TruncateInline(result, inlineMaxChars);

        try
        {
            var spillDir = Path.Combine(workspaceRoot, ".deepseek", "runs", runId, "observations");
            Directory.CreateDirectory(spillDir);

            var safeName = SanitizeFileName(toolName);
            var fileName = safeName + "_" + DateTime.UtcNow.ToString("HHmmss") + ".txt";
            var fullPath = Path.Combine(spillDir, fileName);
            File.WriteAllText(fullPath, result, Encoding.UTF8);

            var rel = Path.GetRelativePath(workspaceRoot, fullPath).Replace('\\', '/');
            return BuildSummary(result, rel, result.Length, inlineMaxChars);
        }
        catch
        {
            return TruncateInline(result, inlineMaxChars);
        }
    }

    private static string BuildSummary(string full, string relativePath, int totalChars, int previewChars)
    {
        var lines = full.Replace("\r\n", "\n").Split('\n');
        var head = string.Join('\n', lines.Take(12));
        var tail = lines.Length > 24
            ? string.Join('\n', lines.Skip(lines.Length - 8))
            : "";

        var sb = new StringBuilder();
        sb.AppendLine($"[Harness] 工具输出已落盘（{totalChars} 字符）");
        sb.AppendLine($"完整文件：{relativePath}");
        sb.AppendLine("摘要（首尾行）：");
        sb.AppendLine("---");
        sb.AppendLine(TruncateInline(head, previewChars / 2));
        if (!string.IsNullOrWhiteSpace(tail))
        {
            sb.AppendLine("…");
            sb.AppendLine(TruncateInline(tail, previewChars / 2));
        }
        sb.AppendLine("---");
        sb.AppendLine("需要更多内容请 read_file 上述路径。");
        return sb.ToString().TrimEnd();
    }

    private static string TruncateInline(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(已截断)";

    private static string SanitizeFileName(string name)
    {
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(s) ? "tool" : s[..Math.Min(s.Length, 48)];
    }
}
