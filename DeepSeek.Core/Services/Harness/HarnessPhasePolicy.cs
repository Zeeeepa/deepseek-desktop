namespace DeepSeekBrowser.Services.Harness;

public static class HarnessPhasePolicy
{
    public static bool AllowsTools(HarnessPhase phase, bool blueprintFinalized) =>
        phase switch
        {
            HarnessPhase.Orient or HarnessPhase.Explore => !blueprintFinalized,
            HarnessPhase.Blueprint or HarnessPhase.Verify => false,
            HarnessPhase.Execute => true,
            _ => false
        };

    public static bool IsReadonlyPhase(HarnessPhase phase) =>
        phase is HarnessPhase.Orient or HarnessPhase.Explore or HarnessPhase.Blueprint or HarnessPhase.Verify;

    public static int ResearchCap(HarnessWorkflow workflow, int maxTurns) =>
        workflow == HarnessWorkflow.Blueprint ? Math.Min(maxTurns - 1, 12) : maxTurns;

    public static string TraceLabel(HarnessPhase phase) =>
        phase switch
        {
            HarnessPhase.Orient => "orient",
            HarnessPhase.Explore => "explore",
            HarnessPhase.Blueprint => "blueprint",
            HarnessPhase.Execute => "execute",
            HarnessPhase.Verify => "verify",
            _ => "unknown"
        };
}
