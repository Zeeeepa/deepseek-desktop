using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>从持久化请求日志汇总账户用量（供管理台账户列表/详情展示）。</summary>
public static class RequestLogAccountStats
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public sealed record Usage(int Total, int Today, long LastTimestamp);

    public static Usage TryGet(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return new Usage(0, 0, 0);

        var path = Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "dsd-api", "request-logs.ndjson");
        if (!File.Exists(path))
            return new Usage(0, 0, 0);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var todayStart = new DateTimeOffset(DateTime.Parse(today + "T00:00:00Z", null,
            System.Globalization.DateTimeStyles.AssumeUniversal)).ToUnixTimeMilliseconds();
        var todayEnd = todayStart + 86_400_000L;
        var total = 0;
        var todayCount = 0;
        long lastTs = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("accountId", out var aid)
                    || !string.Equals(aid.GetString(), accountId, StringComparison.OrdinalIgnoreCase))
                    continue;

                total++;
                if (root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t))
                {
                    if (t > lastTs) lastTs = t;
                    if (t >= todayStart && t < todayEnd) todayCount++;
                }
            }
            catch
            {
                // skip corrupt line
            }
        }

        return new Usage(total, todayCount, lastTs);
    }

    public static IReadOnlyList<AccountTrendPoint> GetTrend(string accountId, int days = 7)
    {
        days = Math.Clamp(days, 1, 90);
        var path = Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "dsd-api", "request-logs.ndjson");
        if (!File.Exists(path))
            return BuildEmptyTrend(days);

        var logs = new List<(long Ts, string Status)>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("accountId", out var aid)
                    || !string.Equals(aid.GetString(), accountId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!root.TryGetProperty("timestamp", out var tsEl) || !tsEl.TryGetInt64(out var ts))
                    continue;
                var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                logs.Add((ts, status));
            }
            catch
            {
                // skip
            }
        }

        var dayMs = 86_400_000L;
        var today = DateTime.UtcNow.Date;
        var trends = new List<AccountTrendPoint>();
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            var dayStart = new DateTimeOffset(day, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var dayEnd = dayStart + dayMs;
            var dayLogs = logs.Where(x => x.Ts >= dayStart && x.Ts < dayEnd).ToList();
            var success = dayLogs.Count(x => string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase));
            var error = dayLogs.Count(x => string.Equals(x.Status, "error", StringComparison.OrdinalIgnoreCase));
            trends.Add(new AccountTrendPoint
            {
                Date = day.ToString("yyyy-MM-dd"),
                Total = dayLogs.Count,
                Info = success,
                Warn = 0,
                Error = error
            });
        }

        return trends;
    }

    private static List<AccountTrendPoint> BuildEmptyTrend(int days)
    {
        var today = DateTime.UtcNow.Date;
        var list = new List<AccountTrendPoint>();
        for (var i = days - 1; i >= 0; i--)
        {
            var day = today.AddDays(-i);
            list.Add(new AccountTrendPoint
            {
                Date = day.ToString("yyyy-MM-dd"),
                Total = 0,
                Info = 0,
                Warn = 0,
                Error = 0
            });
        }

        return list;
    }

    public sealed class AccountTrendPoint
    {
        public string Date { get; init; } = "";
        public int Total { get; init; }
        public int Info { get; init; }
        public int Warn { get; init; }
        public int Error { get; init; }
    }
}
