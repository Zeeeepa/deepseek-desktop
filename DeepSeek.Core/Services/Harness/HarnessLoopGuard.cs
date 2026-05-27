using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>防止同轮内重复相同工具调用（对齐 DeepSeek-TUI LoopGuard）。</summary>
public sealed class HarnessLoopGuard
{
    private const int IdenticalCallBlockThreshold = 3;
    private const int FailureWarnThreshold = 3;
    private const int FailureHaltThreshold = 8;

    private readonly Dictionary<string, int> _callCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);

    public bool TryRecordAttempt(string tool, string argumentsJson, out string? blockMessage)
    {
        var key = tool + "|" + HashArgs(argumentsJson);
        _callCounts.TryGetValue(key, out var count);
        count++;
        _callCounts[key] = count;
        if (count >= IdenticalCallBlockThreshold)
        {
            blockMessage =
                $"已阻止：本轮内相同参数的工具 `{tool}` 已调用 {count} 次。请修改参数或换用其他工具。";
            return false;
        }

        blockMessage = null;
        return true;
    }

    public OutcomeDecision RecordOutcome(string tool, bool ok)
    {
        if (ok)
        {
            _failureCounts[tool] = 0;
            return OutcomeDecision.Continue;
        }

        _failureCounts.TryGetValue(tool, out var failures);
        failures++;
        _failureCounts[tool] = failures;
        if (failures >= FailureHaltThreshold)
        {
            return OutcomeDecision.Halt(
                $"工具 `{tool}` 已连续失败 {failures} 次，请换用其他方法。");
        }

        if (failures == FailureWarnThreshold)
        {
            return OutcomeDecision.Warn(
                $"工具 `{tool}` 已连续失败 {failures} 次。");
        }

        return OutcomeDecision.Continue;
    }

    private static string HashArgs(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var canonical = Canonicalize(doc.RootElement);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(bytes);
        }
        catch
        {
            return argumentsJson.Trim();
        }
    }

    private static string Canonicalize(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",",
            el.EnumerateObject()
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonicalize(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", el.EnumerateArray().Select(Canonicalize)) + "]",
        _ => el.GetRawText()
    };

    public enum OutcomeDecisionKind { Continue, Warn, Halt }

    public readonly record struct OutcomeDecision(OutcomeDecisionKind Kind, string? Message = null)
    {
        public static OutcomeDecision Continue => new(OutcomeDecisionKind.Continue);
        public static OutcomeDecision Warn(string message) => new(OutcomeDecisionKind.Warn, message);
        public static OutcomeDecision Halt(string message) => new(OutcomeDecisionKind.Halt, message);
    }
}
