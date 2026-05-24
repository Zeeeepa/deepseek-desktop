using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public static class AgentAutomationScheduler
{
    public static long? ComputeNextRunUtc(AgentAutomation automation, DateTimeOffset? from = null)
    {
        if (!automation.Enabled)
            return null;

        var trigger = automation.Trigger;
        if (!string.Equals(trigger.Type, "schedule", StringComparison.OrdinalIgnoreCase))
            return null;

        var now = from ?? DateTimeOffset.UtcNow;
        if (!TryParseTime(trigger.ScheduleTimeUtc, out var hour, out var minute))
        {
            hour = 9;
            minute = 0;
        }

        return trigger.SchedulePreset.ToLowerInvariant() switch
        {
            "hourly" => now.AddHours(1).ToUnixTimeMilliseconds(),
            "weekly" => NextWeekly(now, hour, minute, trigger.ScheduleDayOfWeek).ToUnixTimeMilliseconds(),
            _ => NextDaily(now, hour, minute).ToUnixTimeMilliseconds()
        };
    }

    public static void RefreshSchedule(AgentAutomation automation, DateTimeOffset? from = null)
    {
        automation.NextRunAt = ComputeNextRunUtc(automation, from);
    }

    public static bool IsDue(AgentAutomation automation, DateTimeOffset now)
    {
        if (!automation.Enabled || automation.NextRunAt is null)
            return false;

        return now.ToUnixTimeMilliseconds() >= automation.NextRunAt.Value;
    }

    private static DateTimeOffset NextDaily(DateTimeOffset now, int hour, int minute)
    {
        var candidate = new DateTimeOffset(now.Year, now.Month, now.Day, hour, minute, 0, TimeSpan.Zero);
        if (candidate <= now)
            candidate = candidate.AddDays(1);
        return candidate;
    }

    private static DateTimeOffset NextWeekly(DateTimeOffset now, int hour, int minute, int dayOfWeek)
    {
        dayOfWeek = Math.Clamp(dayOfWeek, 0, 6);
        var target = (DayOfWeek)dayOfWeek;
        var daysAhead = ((int)target - (int)now.DayOfWeek + 7) % 7;
        var candidate = new DateTimeOffset(
            now.Year, now.Month, now.Day, hour, minute, 0, TimeSpan.Zero).AddDays(daysAhead);
        if (candidate <= now)
            candidate = candidate.AddDays(7);
        return candidate;
    }

    private static bool TryParseTime(string? text, out int hour, out int minute)
    {
        hour = 9;
        minute = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        return int.TryParse(parts[0], out hour) && int.TryParse(parts[1], out minute);
    }
}
