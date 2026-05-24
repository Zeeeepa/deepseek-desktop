using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public sealed class AgentAutomationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _root;
    private readonly string _listPath;
    private readonly string _runsDir;

    public AgentAutomationStore(string? root = null)
    {
        _root = root ?? Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "automations");
        _listPath = Path.Combine(_root, "automations.json");
        _runsDir = Path.Combine(_root, "runs");
        Directory.CreateDirectory(_runsDir);
    }

    public IReadOnlyList<AgentAutomation> List()
    {
        if (!File.Exists(_listPath))
            return [];

        try
        {
            var json = File.ReadAllText(_listPath);
            var file = JsonSerializer.Deserialize<AgentAutomationListFile>(json, JsonOptions);
            return file?.Automations ?? [];
        }
        catch
        {
            return [];
        }
    }

    public AgentAutomation? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return List().FirstOrDefault(a =>
            string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Save(AgentAutomation automation)
    {
        ArgumentNullException.ThrowIfNull(automation);
        if (string.IsNullOrWhiteSpace(automation.Id))
            automation.Id = "auto_" + Guid.NewGuid().ToString("N")[..12];

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (automation.CreatedAt <= 0)
            automation.CreatedAt = now;
        automation.UpdatedAt = now;

        AgentAutomationScheduler.RefreshSchedule(automation);

        var all = List().ToList();
        var idx = all.FindIndex(a => string.Equals(a.Id, automation.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            all[idx] = automation;
        else
            all.Add(automation);

        WriteAll(all);
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var all = List().ToList();
        var removed = all.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            return false;

        WriteAll(all);
        try
        {
            var dir = Path.Combine(_runsDir, id);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // ignore
        }

        return true;
    }

    public void RecordRun(AgentAutomationRun run, int keepLast = 50)
    {
        var dir = Path.Combine(_runsDir, run.AutomationId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, run.Id + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(run, JsonOptions));

        var files = Directory.EnumerateFiles(dir, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Skip(keepLast)
            .ToList();
        foreach (var old in files)
        {
            try { File.Delete(old); } catch { /* ignore */ }
        }
    }

    public IReadOnlyList<AgentAutomationRun> ListRuns(string automationId, int limit = 30)
    {
        if (string.IsNullOrWhiteSpace(automationId))
            return [];

        var dir = Path.Combine(_runsDir, automationId);
        if (!Directory.Exists(dir))
            return [];

        var runs = new List<AgentAutomationRun>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(limit))
        {
            try
            {
                var run = JsonSerializer.Deserialize<AgentAutomationRun>(File.ReadAllText(path), JsonOptions);
                if (run is not null)
                    runs.Add(run);
            }
            catch
            {
                // skip corrupt
            }
        }

        return runs;
    }

    public void UpdateAutomationAfterRun(AgentAutomation automation, long finishedAt)
    {
        automation.LastRunAt = finishedAt;
        automation.RunCount++;
        AgentAutomationScheduler.RefreshSchedule(automation, DateTimeOffset.FromUnixTimeMilliseconds(finishedAt));
        Save(automation);
    }

    private void WriteAll(List<AgentAutomation> automations)
    {
        Directory.CreateDirectory(_root);
        var file = new AgentAutomationListFile { Automations = automations };
        File.WriteAllText(_listPath, JsonSerializer.Serialize(file, JsonOptions));
    }
}
