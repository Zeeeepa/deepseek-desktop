using System.Text;
using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>.deepseek/approval-rules.yaml — Always Allow glob 规则。</summary>
public static class HarnessApprovalRulesStore
{
    public static bool IsPreApproved(string workspaceRoot, string toolName, string? detail)
    {
        var rules = Load(workspaceRoot);
        if (rules.Count == 0) return false;
        var norm = BuiltinToolExecutor.NormalizeName(toolName).ToLowerInvariant();
        foreach (var rule in rules)
        {
            if (!rule.Tools.Any(t => norm.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (rule.Globs.Count == 0)
                return true;
            if (string.IsNullOrWhiteSpace(detail))
                continue;
            if (rule.Globs.Any(g => GlobMatch(g, detail)))
                return true;
        }

        return false;
    }

    public static void AddAlwaysAllow(string workspaceRoot, string toolPattern, string? pathGlob = "*")
    {
        var path = RulesPath(workspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        lines.Add($"- tool: {toolPattern}");
        lines.Add($"  glob: {pathGlob ?? "*"}");
        lines.Add("  action: allow");
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    private static string RulesPath(string workspaceRoot) =>
        Path.Combine(workspaceRoot, ".deepseek", "approval-rules.yaml");

    private static List<ApprovalRule> Load(string workspaceRoot)
    {
        var path = RulesPath(workspaceRoot);
        if (!File.Exists(path)) return [];
        var rules = new List<ApprovalRule>();
        ApprovalRule? current = null;
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.StartsWith("- tool:", StringComparison.OrdinalIgnoreCase))
            {
                current = new ApprovalRule { Tools = [t["- tool:".Length..].Trim()] };
                rules.Add(current);
            }
            else if (current is not null && t.StartsWith("glob:", StringComparison.OrdinalIgnoreCase))
                current.Globs.Add(t["glob:".Length..].Trim());
        }

        return rules;
    }

    private static bool GlobMatch(string pattern, string text)
    {
        var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(text, rx, RegexOptions.IgnoreCase);
    }

    private sealed class ApprovalRule
    {
        public List<string> Tools { get; init; } = [];
        public List<string> Globs { get; init; } = [];
    }
}
