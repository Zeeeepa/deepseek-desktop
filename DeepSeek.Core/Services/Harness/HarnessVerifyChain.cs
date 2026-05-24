using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessVerifyStep
{
    public string Command { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;
    public bool Optional { get; set; }
    public string? Name { get; set; }
}

public static class HarnessVerifyChain
{
    public static IReadOnlyList<HarnessVerifyStep> Resolve(HarnessPlaybook? playbook, AppConfig config)
    {
        if (playbook?.Verify is { } pbVerify)
        {
            if (pbVerify.Steps.Count > 0)
                return pbVerify.Steps;
            if (!string.IsNullOrWhiteSpace(pbVerify.Command))
            {
                return
                [
                    new HarnessVerifyStep
                    {
                        Command = pbVerify.Command,
                        TimeoutSeconds = pbVerify.TimeoutSeconds,
                        Optional = pbVerify.Optional,
                        Name = "verify"
                    }
                ];
            }
        }

        if (config.AgentVerifyAfterExecute)
        {
            var steps = new List<HarnessVerifyStep>();
            foreach (var cmd in config.AgentVerifyCommands.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                steps.Add(new HarnessVerifyStep
                {
                    Command = cmd.Trim(),
                    TimeoutSeconds = Math.Clamp(config.AgentVerifyTimeoutSeconds, 5, 600),
                    Optional = config.AgentVerifyOptional
                });
            }

            if (steps.Count == 0 && !string.IsNullOrWhiteSpace(config.AgentVerifyCommand))
            {
                steps.Add(new HarnessVerifyStep
                {
                    Command = config.AgentVerifyCommand.Trim(),
                    TimeoutSeconds = Math.Clamp(config.AgentVerifyTimeoutSeconds, 5, 600),
                    Optional = config.AgentVerifyOptional
                });
            }

            if (steps.Count > 0)
                return steps;
        }

        return Array.Empty<HarnessVerifyStep>();
    }

    public static async Task<HarnessVerifyChainResult> RunAsync(
        IReadOnlyList<HarnessVerifyStep> steps,
        string workspaceRoot,
        CancellationToken ct)
    {
        if (steps.Count == 0)
            return new HarnessVerifyChainResult { Skipped = true, CombinedOutput = "（未配置 Verify）" };

        var sb = new System.Text.StringBuilder();
        var allPassed = true;
        var anyRequiredFailed = false;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var label = string.IsNullOrWhiteSpace(step.Name) ? $"step{i + 1}" : step.Name;
            var result = await HarnessVerifyRunner.RunAsync(
                step.Command, workspaceRoot, step.TimeoutSeconds, ct);

            sb.AppendLine($"### Verify · {label}");
            sb.AppendLine(result.Output);
            sb.AppendLine();

            if (!result.Passed)
            {
                allPassed = false;
                if (!step.Optional)
                    anyRequiredFailed = true;
            }

            if (anyRequiredFailed)
                break;
        }

        return new HarnessVerifyChainResult
        {
            Passed = allPassed,
            AnyRequiredFailed = anyRequiredFailed,
            CombinedOutput = sb.ToString().TrimEnd()
        };
    }
}

public sealed class HarnessVerifyChainResult
{
    public bool Skipped { get; init; }
    public bool Passed { get; init; }
    public bool AnyRequiredFailed { get; init; }
    public string CombinedOutput { get; init; } = "";
}
