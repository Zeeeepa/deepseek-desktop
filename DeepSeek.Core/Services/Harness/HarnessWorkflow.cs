namespace DeepSeekBrowser.Services.Harness;

public enum HarnessWorkflow
{
    /// <summary>Explore → Blueprint 两阶段工作流（原 Plan 模式）。</summary>
    Blueprint,

    /// <summary>单阶段 Execute（原 React 模式）。</summary>
    Execute
}

public sealed class HarnessStrategyProfile
{
    public required HarnessWorkflow Workflow { get; init; }
    public required HarnessPhase InitialPhase { get; init; }
}
