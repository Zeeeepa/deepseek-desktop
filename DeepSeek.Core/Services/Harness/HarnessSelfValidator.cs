namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// Harness 输出前自检（借鉴 Harness 4.0 self_validation，由 C# 闸门强制执行）。
/// </summary>
public static class HarnessSelfValidator
{
    private static readonly string[] BlueprintSections =
    [
        "目标", "现状", "步骤", "风险", "验收"
    ];

    public static HarnessValidationResult ValidateBlueprint(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return Fail("Blueprint 输出为空");

        var missing = BlueprintSections
            .Where(s => !ContainsSection(answer, s))
            .ToList();

        if (BlueprintSections.Length - missing.Count < 3)
            return Fail("Blueprint 缺少必要章节：" + string.Join("、", missing));

        if (answer.Trim().Length < 40)
            return Fail("Blueprint 过短，需基于 Explore Observation 展开");

        return Pass();
    }

    public static HarnessValidationResult ValidateExecuteAnswer(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || answer.Length < 20)
            return Fail("Execute 最终回答过短或未给出结果摘要");
        return Pass();
    }

    public static ChatMessage BuildBlueprintRetryMessage(IReadOnlyList<string> issues) => new()
    {
        Role = "user",
        Content =
            "Harness 自检未通过：" + string.Join("；", issues) + "。\n" +
            "请**不要调用工具**，按 Blueprint 结构重新输出：\n" +
            "## 目标\n## 现状摘要\n## 建议步骤\n## 风险与依赖\n## 验收标准"
    };

    private static bool ContainsSection(string text, string keyword) =>
        text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        || text.Contains("## " + keyword, StringComparison.OrdinalIgnoreCase);

    private static HarnessValidationResult Pass() => new() { Passed = true };

    private static HarnessValidationResult Fail(string issue) => new()
    {
        Passed = false,
        Issues = [issue]
    };
}

public sealed class HarnessValidationResult
{
    public bool Passed { get; init; }
    public List<string> Issues { get; init; } = new();
}
