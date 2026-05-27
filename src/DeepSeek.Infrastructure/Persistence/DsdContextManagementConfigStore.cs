using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>上下文管理配置（对齐 Chat2API DEFAULT_CONTEXT_MANAGEMENT_CONFIG）。</summary>
public static class DsdContextManagementConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();

    public sealed class ContextManagementConfigRecord
    {
        public bool Enabled { get; set; }
        public ContextStrategiesRecord Strategies { get; set; } = ContextStrategiesRecord.CreateDefault();
        public List<string> ExecutionOrder { get; set; } = ["slidingWindow", "tokenLimit", "summary"];
    }

    public sealed class ContextStrategiesRecord
    {
        public SlidingWindowRecord SlidingWindow { get; set; } = new();
        public TokenLimitRecord TokenLimit { get; set; } = new();
        public SummaryRecord Summary { get; set; } = new();

        public static ContextStrategiesRecord CreateDefault() => new();
    }

    public sealed class SlidingWindowRecord
    {
        public bool Enabled { get; set; } = true;
        public int MaxMessages { get; set; } = 20;
    }

    public sealed class TokenLimitRecord
    {
        public bool Enabled { get; set; }
        public int MaxTokens { get; set; } = 8000;
    }

    public sealed class SummaryRecord
    {
        public bool Enabled { get; set; }
        public int KeepRecentMessages { get; set; } = 20;
        public string? CustomPrompt { get; set; }
    }

    private static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "context-management-config.json");

    public static ContextManagementConfigRecord Get()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath))
                return new ContextManagementConfigRecord();
            try
            {
                return JsonSerializer.Deserialize<ContextManagementConfigRecord>(File.ReadAllText(FilePath), JsonOptions)
                       ?? new ContextManagementConfigRecord();
            }
            catch
            {
                return new ContextManagementConfigRecord();
            }
        }
    }

    public static ContextManagementConfigRecord Update(Action<ContextManagementConfigRecord> mutate)
    {
        lock (Gate)
        {
            var cfg = Get();
            mutate(cfg);
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(cfg, JsonOptions));
            return cfg;
        }
    }

    public static void ApplyJsonPatch(ContextManagementConfigRecord cfg, JsonElement el)
    {
        if (el.TryGetProperty("enabled", out var enabled))
            cfg.Enabled = enabled.GetBoolean();

        if (el.TryGetProperty("strategies", out var strategies) && strategies.ValueKind == JsonValueKind.Object)
        {
            if (strategies.TryGetProperty("slidingWindow", out var sw) && sw.ValueKind == JsonValueKind.Object)
            {
                if (sw.TryGetProperty("enabled", out var swEnabled))
                    cfg.Strategies.SlidingWindow.Enabled = swEnabled.GetBoolean();
                if (sw.TryGetProperty("maxMessages", out var max) && max.TryGetInt32(out var maxMessages))
                    cfg.Strategies.SlidingWindow.MaxMessages = Math.Max(1, maxMessages);
            }

            if (strategies.TryGetProperty("tokenLimit", out var tl) && tl.ValueKind == JsonValueKind.Object)
            {
                if (tl.TryGetProperty("enabled", out var tlEnabled))
                    cfg.Strategies.TokenLimit.Enabled = tlEnabled.GetBoolean();
                if (tl.TryGetProperty("maxTokens", out var maxTok) && maxTok.TryGetInt32(out var maxTokens))
                    cfg.Strategies.TokenLimit.MaxTokens = Math.Max(1, maxTokens);
            }

            if (strategies.TryGetProperty("summary", out var sum) && sum.ValueKind == JsonValueKind.Object)
            {
                if (sum.TryGetProperty("enabled", out var sumEnabled))
                    cfg.Strategies.Summary.Enabled = sumEnabled.GetBoolean();
                if (sum.TryGetProperty("keepRecentMessages", out var keep) && keep.TryGetInt32(out var keepRecent))
                    cfg.Strategies.Summary.KeepRecentMessages = Math.Max(1, keepRecent);
                if (sum.TryGetProperty("customPrompt", out var prompt))
                {
                    cfg.Strategies.Summary.CustomPrompt = prompt.ValueKind == JsonValueKind.String
                        ? prompt.GetString()
                        : null;
                }
            }
        }

        if (el.TryGetProperty("executionOrder", out var order) && order.ValueKind == JsonValueKind.Array)
        {
            var next = new List<string>();
            foreach (var item in order.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        next.Add(s);
                }
            }

            if (next.Count > 0)
                cfg.ExecutionOrder = next;
        }
    }
}
