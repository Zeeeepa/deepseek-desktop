namespace DeepSeekBrowser.Services.Harness.Interop;

/// <summary>
/// 市场标准 SKILL.md 在 DSD Harness 中的适配视图（非拷贝 skill 包，只读加载）。
/// </summary>
public sealed class HarnessSkill
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Body { get; set; } = "";
    public string Source { get; set; } = "unknown";
    public string FilePath { get; set; } = "";
}

public sealed class HarnessSkillSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Source { get; init; }
    public required string FilePath { get; init; }
}
