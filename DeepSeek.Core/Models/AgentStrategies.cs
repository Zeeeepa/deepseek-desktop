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
}
