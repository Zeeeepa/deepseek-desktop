using System.IO;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

public static class ConfigStore
{
    private static readonly object SaveGate = new();
    private static string? _cachedJson;
    private static long _cachedWriteTicks = -1;
    private static string? _cachedConfigPath;

    private static string ConfigDir
    {
        get
        {
            DeepSeekDesktopApp.EnsureLegacyAppDataMigrated();
            return DeepSeekDesktopApp.LocalAppDataRoot;
        }
    }

    public static string ConfigDirectory => ConfigDir;
    public static string ConfigFilePath => Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AppConfig Load()
    {
        lock (SaveGate)
        {
            try
            {
                var configPath = ConfigFilePath;
                if (!string.Equals(_cachedConfigPath, configPath, StringComparison.OrdinalIgnoreCase))
                    InvalidateCache();
                _cachedConfigPath = configPath;

                if (!File.Exists(configPath))
                {
                    InvalidateCache();
                    return new AppConfig();
                }

                var writeTicks = File.GetLastWriteTimeUtc(configPath).Ticks;
                if (_cachedJson is not null && writeTicks == _cachedWriteTicks)
                    return DeserializeCached(_cachedJson);

                var json = File.ReadAllText(configPath, Encoding.UTF8);
                json = ConfigFileRepair.PrepareJsonForDeserialize(json, ConfigDir);
                var cfg = FinishLoad(json);
                _cachedWriteTicks = writeTicks;
                return cfg;
            }
            catch
            {
                InvalidateCache();
                return new AppConfig();
            }
        }
    }

    private static void InvalidateCache()
    {
        _cachedJson = null;
        _cachedWriteTicks = -1;
        _cachedConfigPath = null;
    }

    private static AppConfig DeserializeCached(string json)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        ConfigFileRepair.MigrateAndRepair(cfg, ConfigDir, out _);
        return cfg;
    }

    private static AppConfig FinishLoad(string json)
    {
        var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        if (ConfigFileRepair.MigrateAndRepair(cfg, ConfigDir, out var repaired))
        {
            Save(cfg);
            _cachedJson = JsonSerializer.Serialize(cfg, JsonOptions);
            return cfg;
        }

        _cachedJson = json;
        return cfg;
    }

    public static void Save(AppConfig config)
    {
        lock (SaveGate)
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            WriteAtomic(ConfigFilePath, json);
            _cachedJson = json;
            _cachedWriteTicks = File.Exists(ConfigFilePath)
                ? File.GetLastWriteTimeUtc(ConfigFilePath).Ticks
                : -1;
        }
    }

    private static void WriteAtomic(string targetPath, string content)
    {
        var temp = targetPath + ".tmp";
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (File.Exists(temp))
                    File.Delete(temp);

                File.WriteAllText(temp, content, Encoding.UTF8);

                if (File.Exists(targetPath))
                    File.Replace(temp, targetPath, null, ignoreMetadataErrors: true);
                else
                    File.Move(temp, targetPath);

                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(30 * (attempt + 1));
            }
        }

        File.WriteAllText(targetPath, content, Encoding.UTF8);
        try
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        catch
        {
            // ignore cleanup failure
        }
    }
}
