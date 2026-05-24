namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// DSD Harness 原创 Playbook（剧本）：轻量工作流清单，非 agent-skills 拷贝。
/// </summary>
public sealed class HarnessPlaybook
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>blueprint | orient | execute（可选，覆盖默认 strategy）</summary>
    public string? Strategy { get; set; }

    /// <summary>追加到 system prompt 的原创指令。</summary>
    public string? SystemAppend { get; set; }

    public List<string> Steps { get; set; } = new();

    public HarnessPlaybookVerify? Verify { get; set; }
}

public sealed class HarnessPlaybookVerify
{
    public string Command { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 120;

    public bool Optional { get; set; }

    /// <summary>多步 Verify 链（优先于单条 Command）。</summary>
    public List<HarnessVerifyStep> Steps { get; set; } = new();
}

public sealed class HarnessPlaybookSummary
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Strategy { get; init; }
    public bool HasVerify { get; init; }
    public int VerifyStepCount { get; init; }
    public string Source { get; init; } = "user";
}
