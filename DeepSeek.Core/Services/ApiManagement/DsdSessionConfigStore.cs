using System.Text.Json;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>会话配置（对齐 Chat2API SessionConfig / DEFAULT_SESSION_CONFIG）。</summary>
public static class DsdSessionConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();

    public sealed class SessionConfigRecord
    {
        public int SessionTimeout { get; set; } = 30;
        public int MaxMessagesPerSession { get; set; } = 50;
        public bool DeleteAfterTimeout { get; set; }
        public int MaxSessionsPerAccount { get; set; } = 3;
    }

    private static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "session-config.json");

    public static SessionConfigRecord Get()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath))
                return new SessionConfigRecord();
            try
            {
                return JsonSerializer.Deserialize<SessionConfigRecord>(File.ReadAllText(FilePath), JsonOptions)
                       ?? new SessionConfigRecord();
            }
            catch
            {
                return new SessionConfigRecord();
            }
        }
    }

    public static SessionConfigRecord Update(Action<SessionConfigRecord> mutate)
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
}
