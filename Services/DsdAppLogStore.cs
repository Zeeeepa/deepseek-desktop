using System.IO;
using System.Text.Json;

namespace DeepSeekBrowser.Services;

/// <summary>管理台应用日志（对齐 Chat2API AppLogManager，供 logs:* IPC）。</summary>
public sealed class DsdAppLogStore
{
    private static DsdAppLogStore? _instance;
    private readonly object _gate = new();
    private readonly string _logFile;
    private readonly List<AppLogEntry> _logs = new();
    private bool _loaded;

    public static DsdAppLogStore Instance => _instance ??= new DsdAppLogStore();

    private DsdAppLogStore()
    {
        var dir = Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "dsd-api");
        Directory.CreateDirectory(dir);
        _logFile = Path.Combine(dir, "app-logs.ndjson");
    }

    public void Add(string level, string message, Dictionary<string, string>? meta = null)
    {
        var entry = new AppLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = level,
            Message = message,
            AccountId = meta?.GetValueOrDefault("accountId"),
            ProviderId = meta?.GetValueOrDefault("providerId")
        };

        lock (_gate)
        {
            EnsureLoaded();
            _logs.Add(entry);
            if (_logs.Count > 5000)
                _logs.RemoveRange(0, _logs.Count - 5000);
            try
            {
                File.AppendAllText(_logFile, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch
            {
                // ignore persist failure
            }
        }

        DsdApiIpcEventHub.Publish("logs:newLog", entry);
    }

    public IReadOnlyList<AppLogEntry> GetLogs(AppLogFilter? filter = null)
    {
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<AppLogEntry> q = _logs.OrderByDescending(x => x.Timestamp);
            if (filter?.Level is { } lv && !string.Equals(lv, "all", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => string.Equals(x.Level, lv, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(filter?.Keyword))
                q = q.Where(x => x.Message.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase));
            if (filter?.StartTime is > 0)
                q = q.Where(x => x.Timestamp >= filter.StartTime);
            if (filter?.EndTime is > 0)
                q = q.Where(x => x.Timestamp <= filter.EndTime);

            var limit = filter?.Limit is > 0 ? filter.Limit.Value : 200;
            var offset = filter?.Offset is > 0 ? filter.Offset.Value : 0;
            return q.Skip(offset).Take(limit).ToList();
        }
    }

    public AppLogEntry? GetById(string id)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _logs.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        }
    }

    public object GetStats()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var todayStart = new DateTimeOffset(DateTime.Parse(today + "T00:00:00Z",
                null, System.Globalization.DateTimeStyles.AssumeUniversal)).ToUnixTimeMilliseconds();
            var todayEnd = todayStart + 86_400_000L;
            var todayLogs = _logs.Where(x => x.Timestamp >= todayStart && x.Timestamp < todayEnd).ToList();
            return new
            {
                total = _logs.Count,
                today = todayLogs.Count,
                info = _logs.Count(x => x.Level == "info"),
                warn = _logs.Count(x => x.Level == "warn"),
                error = _logs.Count(x => x.Level == "error"),
                todayInfo = todayLogs.Count(x => x.Level == "info"),
                todayWarn = todayLogs.Count(x => x.Level == "warn"),
                todayError = todayLogs.Count(x => x.Level == "error")
            };
        }
    }

    public object[] GetTrend(int days = 7)
    {
        days = Math.Clamp(days, 1, 90);
        lock (_gate)
        {
            EnsureLoaded();
            var today = DateTime.UtcNow.Date;
            var dayMs = 86_400_000L;
            var trends = new List<object>();
            for (var i = days - 1; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var start = new DateTimeOffset(day, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var end = start + dayMs;
                var dayLogs = _logs.Where(x => x.Timestamp >= start && x.Timestamp < end).ToList();
                trends.Add(new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    total = dayLogs.Count,
                    info = dayLogs.Count(x => x.Level == "info"),
                    warn = dayLogs.Count(x => x.Level == "warn"),
                    error = dayLogs.Count(x => x.Level == "error")
                });
            }

            return trends.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _logs.Clear();
            try
            {
                if (File.Exists(_logFile))
                    File.Delete(_logFile);
            }
            catch
            {
                // ignore
            }
        }
    }

    public string Export(string format = "json")
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (string.Equals(format, "txt", StringComparison.OrdinalIgnoreCase))
                return string.Join(Environment.NewLine, _logs.Select(l => $"[{l.Level}] {l.Message}"));

            return JsonSerializer.Serialize(_logs);
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!File.Exists(_logFile)) return;
        foreach (var line in File.ReadLines(_logFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<AppLogEntry>(line);
                if (entry is not null)
                    _logs.Add(entry);
            }
            catch
            {
                // skip
            }
        }
    }

    public sealed class AppLogEntry
    {
        public string Id { get; set; } = "";
        public long Timestamp { get; set; }
        public string Level { get; set; } = "info";
        public string Message { get; set; } = "";
        public string? AccountId { get; set; }
        public string? ProviderId { get; set; }
    }

    public sealed class AppLogFilter
    {
        public string? Level { get; init; }
        public string? Keyword { get; init; }
        public long? StartTime { get; init; }
        public long? EndTime { get; init; }
        public int? Limit { get; init; }
        public int? Offset { get; init; }
    }
}
