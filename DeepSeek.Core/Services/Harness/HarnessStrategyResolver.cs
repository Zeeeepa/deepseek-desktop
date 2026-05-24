using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessStrategyResolver
{
    public static HarnessStrategyProfile Resolve(string? strategy)
    {
        var s = (strategy ?? AgentStrategies.Execute).Trim().ToLowerInvariant();
        return s switch
        {
            "plan" or "blueprint" => new HarnessStrategyProfile
            {
                Workflow = HarnessWorkflow.Blueprint,
                InitialPhase = HarnessPhase.Explore
            },
            "orient" => new HarnessStrategyProfile
            {
                Workflow = HarnessWorkflow.Blueprint,
                InitialPhase = HarnessPhase.Orient
            },
            "react" or "execute" => new HarnessStrategyProfile
            {
                Workflow = HarnessWorkflow.Execute,
                InitialPhase = HarnessPhase.Execute
            },
            _ => new HarnessStrategyProfile
            {
                Workflow = HarnessWorkflow.Execute,
                InitialPhase = HarnessPhase.Execute
            }
        };
    }

    public static bool IsBlueprintWorkflow(string? strategy) =>
        Resolve(strategy).Workflow == HarnessWorkflow.Blueprint;

    public static string Normalize(string? strategy)
    {
        var s = (strategy ?? AgentStrategies.Execute).Trim().ToLowerInvariant();
        return s switch
        {
            "plan" or "blueprint" => AgentStrategies.Blueprint,
            AgentStrategies.Orient => AgentStrategies.Orient,
            _ => AgentStrategies.Execute
        };
    }
}
