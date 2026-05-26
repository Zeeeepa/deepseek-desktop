using System.IO;
using System.Text.Json;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 内嵌 DSD API 请求日志（对齐 DSD API RequestLogManager 形状，供仪表盘与日志页使用）。
/// </summary>
public sealed class DsdApiRequestLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static DsdApiRequestLogStore? _instance;
    private readonly object _gate = new();
    private readonly string _logFile;
    private readonly List<RequestLogEntry> _logs = new();
    private RequestLogConfig _config = RequestLogConfig.CreateDefault();
    private bool _loaded;

    public static DsdApiRequestLogStore Instance =>
        _instance ??= new DsdApiRequestLogStore();

    private DsdApiRequestLogStore()
    {
        var dir = Path.Combine(DeepSeekDesktopApp.LocalAppDataRoot, "dsd-api");
        Directory.CreateDirectory(dir);
        _logFile = Path.Combine(dir, "request-logs.ndjson");
    }

    public void Configure(RequestLogConfig config)
    {
        lock (_gate)
        {
            _config = config;
            Trim();
        }
    }

    public RequestLogConfig GetConfig()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _config;
        }
    }

    public RequestLogEntry Add(RequestLogDraft draft)
    {
        var entry = new RequestLogEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = draft.Success ? "success" : "error",
            StatusCode = draft.StatusCode,
            Method = draft.Method,
            Url = draft.Url,
            Model = draft.Model,
            ActualModel = draft.ActualModel,
            ProviderId = draft.ProviderId,
            ProviderName = draft.ProviderName,
            AccountId = draft.AccountId,
            AccountName = draft.AccountName,
            UserInput = Truncate(draft.UserInput, 200),
            WebSearch = draft.WebSearch,
            ResponseStatus = draft.ResponseStatus,
            ResponsePreview = Truncate(draft.ResponsePreview, 500),
            Latency = draft.LatencyMs,
            IsStream = draft.IsStream,
            ErrorMessage = draft.ErrorMessage
        };

        lock (_gate)
        {
            EnsureLoaded();
            if (!_config.Enabled || _config.MaxEntries <= 0)
                return entry;

            _logs.Add(entry);
            Trim();
            Persist();

            if (!string.IsNullOrWhiteSpace(draft.AccountId)
                && !string.Equals(draft.AccountId, "embedded", StringComparison.OrdinalIgnoreCase))
            {
                if (draft.Success)
                    ProviderAccountStore.RecordSuccess(draft.AccountId);
                else
                    ProviderAccountStore.RecordFailure(draft.AccountId);
            }
        }

        if (_config.Enabled && _config.MaxEntries > 0)
            DsdApiIpcEventHub.Publish("requestLogs:new", entry);

        return entry;
    }

    public IReadOnlyList<RequestLogEntry> GetLogs(RequestLogQuery? query = null)
    {
        lock (_gate)
        {
            EnsureLoaded();
            IEnumerable<RequestLogEntry> q = _logs.OrderByDescending(x => x.Timestamp);
            if (query?.Status is { } st && !string.Equals(st, "all", StringComparison.OrdinalIgnoreCase))
                q = q.Where(x => string.Equals(x.Status, st, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(query?.ProviderId))
                q = q.Where(x => string.Equals(x.ProviderId, query.ProviderId, StringComparison.OrdinalIgnoreCase));

            var limit = query?.Limit is > 0 ? query.Limit.Value : 200;
            return q.Take(limit).ToList();
        }
    }

    public RequestLogEntry? GetById(string id)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _logs.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            _logs.Clear();
            Persist();
        }
    }

    public RequestLogStats GetStats()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var todayStart = new DateTimeOffset(DateTime.Parse(today + "T00:00:00Z")).ToUnixTimeMilliseconds();
            var todayEnd = todayStart + 86_400_000;
            var todayLogs = _logs.Where(x => x.Timestamp >= todayStart && x.Timestamp < todayEnd).ToList();

            return new RequestLogStats
            {
                Total = _logs.Count,
                Success = _logs.Count(x => x.Status == "success"),
                Error = _logs.Count(x => x.Status == "error"),
                TodayTotal = todayLogs.Count,
                TodaySuccess = todayLogs.Count(x => x.Status == "success"),
                TodayError = todayLogs.Count(x => x.Status == "error")
            };
        }
    }

    public IReadOnlyList<RequestLogTrendPoint> GetTrend(int days = 7)
    {
        lock (_gate)
        {
            EnsureLoaded();
            days = Math.Clamp(days, 1, 90);
            var dayMs = 86_400_000L;
            var today = DateTime.UtcNow.Date;
            var trends = new List<RequestLogTrendPoint>();

            for (var i = days - 1; i >= 0; i--)
            {
                var day = today.AddDays(-i);
                var dayStart = new DateTimeOffset(day, TimeSpan.Zero).ToUnixTimeMilliseconds();
                var dayEnd = dayStart + dayMs;
                var dayLogs = _logs.Where(x => x.Timestamp >= dayStart && x.Timestamp < dayEnd).ToList();
                var successLogs = dayLogs.Where(x => x.Status == "success").ToList();
                var totalLatency = successLogs.Sum(x => x.Latency);

                trends.Add(new RequestLogTrendPoint
                {
                    Date = day.ToString("yyyy-MM-dd"),
                    Total = dayLogs.Count,
                    Success = successLogs.Count,
                    Error = dayLogs.Count(x => x.Status == "error"),
                    AvgLatency = successLogs.Count > 0 ? (int)Math.Round(totalLatency / (double)successLogs.Count) : 0
                });
            }

            return trends;
        }
    }

    public object BuildPersistentStatistics()
    {
        lock (_gate)
        {
            EnsureLoaded();
            var stats = GetStats();
            var trend = GetTrend(30);
            var dailyStats = trend.ToDictionary(
                t => t.Date,
                t => (object)new
                {
                    date = t.Date,
                    totalRequests = t.Total,
                    successRequests = t.Success,
                    failedRequests = t.Error,
                    totalLatency = t.Success > 0 ? (long)t.AvgLatency * t.Success : 0L,
                    modelUsage = new Dictionary<string, int>(),
                    providerUsage = new Dictionary<string, int>()
                });

            var modelUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var providerUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var accountUsage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            long totalLatency = 0;

            foreach (var log in _logs)
            {
                if (!string.IsNullOrWhiteSpace(log.Model))
                    modelUsage[log.Model] = modelUsage.GetValueOrDefault(log.Model) + 1;
                if (!string.IsNullOrWhiteSpace(log.ProviderId))
                    providerUsage[log.ProviderId] = providerUsage.GetValueOrDefault(log.ProviderId) + 1;
                if (!string.IsNullOrWhiteSpace(log.AccountId))
                    accountUsage[log.AccountId] = accountUsage.GetValueOrDefault(log.AccountId) + 1;
                if (log.Status == "success")
                    totalLatency += log.Latency;
            }

            return new
            {
                totalRequests = stats.Total,
                successRequests = stats.Success,
                failedRequests = stats.Error,
                totalLatency,
                lastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                modelUsage,
                providerUsage,
                accountUsage,
                dailyStats
            };
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
                var entry = JsonSerializer.Deserialize<RequestLogEntry>(line, JsonOptions);
                if (entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
                    _logs.Add(entry);
            }
            catch
            {
                // skip corrupt line
            }
        }

        _logs.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        Trim();
    }

    private void Trim()
    {
        if (_config.MaxEntries <= 0)
        {
            _logs.Clear();
            return;
        }

        if (_logs.Count <= _config.MaxEntries) return;
        var skip = _logs.Count - _config.MaxEntries;
        _logs.RemoveRange(0, skip);
    }

    private void Persist()
    {
        try
        {
            var lines = _logs.Select(x => JsonSerializer.Serialize(x, JsonOptions));
            File.WriteAllLines(_logFile, lines);
        }
        catch
        {
            // best effort
        }
    }

    private static string? Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text;
        return text[..max];
    }

    public sealed class RequestLogDraft
    {
        public bool Success { get; init; }
        public int StatusCode { get; init; } = 200;
        public int ResponseStatus { get; init; } = 200;
        public string Method { get; init; } = "POST";
        public string Url { get; init; } = "/v1/chat/completions";
        public string Model { get; init; } = "";
        public string? ActualModel { get; init; }
        public string? ProviderId { get; init; }
        public string? ProviderName { get; init; }
        public string? AccountId { get; init; }
        public string? AccountName { get; init; }
        public string? UserInput { get; init; }
        public bool WebSearch { get; init; }
        public string? ResponsePreview { get; init; }
        public long LatencyMs { get; init; }
        public bool IsStream { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed class RequestLogQuery
    {
        public string? Status { get; init; }
        public string? ProviderId { get; init; }
        public int? Limit { get; init; }
    }

    public sealed class RequestLogConfig
    {
        public bool Enabled { get; set; } = true;
        public int MaxEntries { get; set; } = 500;
        public bool IncludeBodies { get; set; }
        public int MaxBodyChars { get; set; } = 8000;
        public bool RedactSensitiveData { get; set; } = true;

        public static RequestLogConfig CreateDefault() => new();

        public static RequestLogConfig FromJson(JsonElement el)
        {
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return CreateDefault();
            try
            {
                var cfg = JsonSerializer.Deserialize<RequestLogConfig>(el.GetRawText(), JsonOptions);
                return cfg ?? CreateDefault();
            }
            catch
            {
                return CreateDefault();
            }
        }
    }

    public sealed class RequestLogEntry
    {
        public string Id { get; set; } = "";
        public long Timestamp { get; set; }
        public string Status { get; set; } = "success";
        public int StatusCode { get; set; }
        public string Method { get; set; } = "POST";
        public string Url { get; set; } = "";
        public string Model { get; set; } = "";
        public string? ActualModel { get; set; }
        public string? ProviderId { get; set; }
        public string? ProviderName { get; set; }
        public string? AccountId { get; set; }
        public string? AccountName { get; set; }
        public string? UserInput { get; set; }
        public bool? WebSearch { get; set; }
        public int ResponseStatus { get; set; }
        public string? ResponsePreview { get; set; }
        public long Latency { get; set; }
        public bool IsStream { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class RequestLogStats
    {
        public int Total { get; set; }
        public int Success { get; set; }
        public int Error { get; set; }
        public int TodayTotal { get; set; }
        public int TodaySuccess { get; set; }
        public int TodayError { get; set; }
    }

    public sealed class RequestLogTrendPoint
    {
        public string Date { get; set; } = "";
        public int Total { get; set; }
        public int Success { get; set; }
        public int Error { get; set; }
        public int AvgLatency { get; set; }
    }
}
