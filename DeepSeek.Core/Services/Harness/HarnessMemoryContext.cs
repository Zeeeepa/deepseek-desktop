namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// DSD Harness 原创记忆上下文（借鉴 Harness 4.0 L0–L3 分层思想，非拷贝其 YAML 内容）。
/// </summary>
public sealed class HarnessMemoryContext
{
    public string DomainId { get; init; } = "general";

    public string DomainName { get; init; } = "通用";

    public string? L0CoreExcerpt { get; init; }

    public string? L2Behavior { get; init; }

    public string? L1Context { get; init; }

    public string? L3Cognitive { get; init; }

    public string? CheckpointSummary { get; init; }

    public IReadOnlyList<string> PendingItems { get; init; } = Array.Empty<string>();

    public string? Pitfalls { get; init; }

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(L0CoreExcerpt)
        && string.IsNullOrWhiteSpace(L2Behavior)
        && string.IsNullOrWhiteSpace(L1Context)
        && string.IsNullOrWhiteSpace(L3Cognitive)
        && string.IsNullOrWhiteSpace(CheckpointSummary)
        && PendingItems.Count == 0
        && string.IsNullOrWhiteSpace(Pitfalls);

    public string BuildPromptSection()
    {
        if (IsEmpty) return "";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【DSD Harness 记忆层 · Domain: " + DomainName + " (" + DomainId + ")】");

        if (!string.IsNullOrWhiteSpace(CheckpointSummary))
        {
            sb.AppendLine("会话检查点：");
            sb.AppendLine(CheckpointSummary.Trim());
            if (PendingItems.Count > 0)
            {
                sb.AppendLine("待续事项：");
                foreach (var item in PendingItems.Take(8))
                    sb.AppendLine("- " + item);
            }
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(L2Behavior))
        {
            sb.AppendLine("L2 行为偏好：");
            sb.AppendLine(TrimForPrompt(L2Behavior, 1200));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(L3Cognitive))
        {
            sb.AppendLine("L3 领域认知：");
            sb.AppendLine(TrimForPrompt(L3Cognitive, 1500));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(L1Context))
        {
            sb.AppendLine("L1 当前情境：");
            sb.AppendLine(TrimForPrompt(L1Context, 1000));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(L0CoreExcerpt))
        {
            sb.AppendLine("L0 驾驭约束（摘要）：");
            sb.AppendLine(TrimForPrompt(L0CoreExcerpt, 800));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(Pitfalls))
        {
            sb.AppendLine("避坑指南：");
            sb.AppendLine(TrimForPrompt(Pitfalls, 600));
        }

        return sb.ToString().TrimEnd();
    }

    private static string TrimForPrompt(string text, int max) =>
        text.Length <= max ? text : text[..max] + "\n…(记忆已截断)";
}
