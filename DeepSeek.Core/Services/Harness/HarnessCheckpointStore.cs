using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessCheckpoint
{
    public string LastSession { get; set; } = "";

    public string Summary { get; set; } = "";

    public string? CurrentMilestone { get; set; }

    public string? DomainId { get; set; }

    public string? LastPhase { get; set; }

    public List<string> CompletedItems { get; set; } = new();

    public List<string> PendingItems { get; set; } = new();

    public string? NextContinuation { get; set; }
}

public static class HarnessCheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string CheckpointPath =>
        Path.Combine(AgentDesktopConfigSync.HomeDirectory, "session", "checkpoint.json");

    public static HarnessCheckpoint Load()
    {
        try
        {
            if (!File.Exists(CheckpointPath))
                return new HarnessCheckpoint();
            return JsonSerializer.Deserialize<HarnessCheckpoint>(File.ReadAllText(CheckpointPath), JsonOptions)
                   ?? new HarnessCheckpoint();
        }
        catch
        {
            return new HarnessCheckpoint();
        }
    }

    public static void Save(HarnessCheckpoint checkpoint)
    {
        var dir = Path.GetDirectoryName(CheckpointPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(CheckpointPath, JsonSerializer.Serialize(checkpoint, JsonOptions));
    }

    public static void UpdateAfterRun(
        string userPrompt,
        string answer,
        HarnessDomainMatch domain,
        HarnessPhase phase,
        bool blueprintFinalized)
    {
        var cp = Load();
        cp.LastSession = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        cp.DomainId = domain.Id;
        cp.LastPhase = HarnessPhasePolicy.TraceLabel(phase);
        cp.Summary = BuildSummary(userPrompt, answer);
        cp.CurrentMilestone = blueprintFinalized ? "Blueprint 已输出" : HarnessPhasePolicy.TraceLabel(phase);

        cp.CompletedItems.Insert(0, "✅ " + DateTime.Now.ToString("MM-dd HH:mm") + " · " + Truncate(userPrompt, 60));
        cp.CompletedItems = cp.CompletedItems.Take(20).ToList();

        if (blueprintFinalized && answer.Contains("建议步骤", StringComparison.Ordinal))
            cp.PendingItems.Insert(0, "按 Blueprint 建议步骤进入 Execute");
        cp.PendingItems = cp.PendingItems.Take(10).ToList();

        cp.NextContinuation = "领域=" + domain.Name + "；上次阶段=" + cp.LastPhase;
        Save(cp);
    }

    private static string BuildSummary(string prompt, string answer)
    {
        var p = Truncate(prompt.Trim(), 80);
        var a = Truncate(answer.Replace('\n', ' ').Trim(), 120);
        return string.IsNullOrWhiteSpace(a) ? p : p + " → " + a;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
