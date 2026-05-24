namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// DSD Harness 原创阶段：Model 推理 + Harness 闸门按 Phase 切换工具与提示。
/// </summary>
public enum HarnessPhase
{
    /// <summary>定向：理解目标与工作区边界（可选入口）。</summary>
    Orient,

    /// <summary>探索：只读工具调研事实。</summary>
    Explore,

    /// <summary>蓝图：无工具，输出结构化方案。</summary>
    Blueprint,

    /// <summary>执行：读写与 shell（受审批约束）。</summary>
    Execute,

    /// <summary>验证：Harness 运行验收命令，Model 总结结果。</summary>
    Verify
}
