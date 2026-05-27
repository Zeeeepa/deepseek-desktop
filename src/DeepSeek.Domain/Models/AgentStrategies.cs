namespace DeepSeekBrowser.Models;

public static class AgentStrategies
{
    /// <summary>Blueprint 工作流：Explore → Blueprint（只读调研 + 结构化方案）。</summary>
    public const string Blueprint = "blueprint";

    /// <summary>Execute 工作流：单阶段执行（读写 + shell）。</summary>
    public const string Execute = "execute";

    /// <summary>兼容旧配置/UI。</summary>
    public const string Plan = "plan";

    /// <summary>兼容旧配置/UI。</summary>
    public const string React = "react";

    /// <summary>Orient 入口：Orient → Explore → Blueprint。</summary>
    public const string Orient = "orient";

    /// <summary>多智能体 SOP：PM → Architect → Engineer → Reviewer。</summary>
    public const string Team = "team";

    /// <summary>AutoGen 式并行 Explore 扇出 + 汇总。</summary>
    public const string ParallelExplore = "parallel-explore";

    /// <summary>CAMEL 式 Advocate / Critic 辩论。</summary>
    public const string Debate = "debate";

    /// <summary>MetaGPT 式软件工厂：文档 + 实现 + git + 交付报告。</summary>
    public const string SoftwareFactory = "software-factory";

    public const string GraphPrefix = "graph:";

    public static bool IsGraph(string? strategy) =>
        strategy?.StartsWith(GraphPrefix, StringComparison.OrdinalIgnoreCase) == true;

    public static string? ParseGraphId(string? strategy)
    {
        if (!IsGraph(strategy)) return null;
        var id = strategy![GraphPrefix.Length..].Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public static string GraphStrategy(string graphId) => GraphPrefix + graphId;
}
