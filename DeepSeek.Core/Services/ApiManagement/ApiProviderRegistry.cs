using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

public static class ApiProviderRegistry
{
    private static readonly object SaveGate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string ProvidersFilePath =>
        Path.Combine(ConfigStore.ConfigDirectory, "api-providers.json");

    public static IReadOnlyList<ApiProviderEntry> LoadAll(AppConfig config)
    {
        var fromDisk = LoadFromFile();
        var merged = fromDisk.Count > 0
            ? Merge(config, fromDisk).ToList()
            : config.ApiProviders is { Count: > 0 }
                ? config.ApiProviders.ToList()
                : new List<ApiProviderEntry>();

        EnsureDeepSeekDefault(config, merged);
        EnsureBuiltinCatalogEntries(config, merged);
        return merged;
    }

    /// <summary>仅补全已存在内置项的模型列表，不自动创建无账户的内置供应商。</summary>
    private static void EnsureBuiltinCatalogEntries(AppConfig config, List<ApiProviderEntry> merged)
    {
        foreach (var builtin in BuiltinProviderCatalog.All)
        {
            var existing = merged.FirstOrDefault(p =>
                string.Equals(p.Id, builtin.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                continue;

            if (existing.Models.Count == 0 && builtin.Models.Length > 0)
                existing.Models = builtin.Models.ToList();
        }
    }

    private static void EnsureDeepSeekDefault(AppConfig config, List<ApiProviderEntry> merged)
    {
        if (merged.Any(p => string.Equals(p.Id, "deepseek", StringComparison.OrdinalIgnoreCase)))
            return;

        if (!ProviderAvailabilitySync.HasAnyAccount("deepseek")
            && string.IsNullOrWhiteSpace(config.WebUserToken?.Trim()))
            return;

        merged.Insert(0, CreateDefaultDeepSeek(config));
        if (File.Exists(ProvidersFilePath) && ProviderAvailabilitySync.HasAnyAccount("deepseek"))
            SaveProviders(merged);
    }

    public static ApiProviderEntry? Get(AppConfig config, string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return LoadAll(config).FirstOrDefault(p => p.DefaultForAgent)
                   ?? LoadAll(config).FirstOrDefault(p => p.Enabled)
                   ?? CreateDefaultDeepSeek(config);

        return LoadAll(config).FirstOrDefault(p =>
                   string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase))
               ?? (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase)
                   ? CreateDefaultDeepSeek(config)
                   : null);
    }

    public static ApiProviderEntry CreateDefaultDeepSeek(AppConfig config) => new()
    {
        Id = "deepseek",
        DisplayName = "DeepSeek",
        Kind = ApiProviderKinds.BuiltinWeb,
        RouteMode = ApiRouteModes.EmbeddedWeb,
        Enabled = true,
        DefaultForAgent = true,
        DefaultForChat = true,
        ModelMappings = config.ModelMappings.Count > 0
            ? new List<ModelMappingEntry>(config.ModelMappings)
            : new List<ModelMappingEntry>()
    };

    public static string? ResolveApiKey(ApiProviderEntry provider) =>
        CredentialVault.TryGet(provider.Id, "api_key");

    public static void SaveProviders(IReadOnlyList<ApiProviderEntry> providers)
    {
        lock (SaveGate)
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            var json = JsonSerializer.Serialize(providers, JsonOptions);
            WriteProvidersAtomic(ProvidersFilePath, json);
        }
    }

    private static void WriteProvidersAtomic(string targetPath, string content)
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

    public static void AddOrUpdate(AppConfig config, ApiProviderEntry entry)
    {
        var list = LoadAll(config).ToList();
        var idx = list.FindIndex(p => string.Equals(p.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            list[idx] = entry;
        else
            list.Add(entry);
        SaveProviders(list);
    }

    public static bool Delete(string providerId)
    {
        if (!File.Exists(ProvidersFilePath))
            return false;
        var list = LoadFromFile();
        var removed = list.RemoveAll(p => string.Equals(p.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
            return false;
        SaveProviders(list);
        return true;
    }

    /// <summary>复制自定义供应商配置（对齐 Chat2API CustomProviderManager.duplicate）。</summary>
    public static ApiProviderEntry Duplicate(AppConfig config, string sourceId, string? newName = null)
    {
        var source = Get(config, sourceId)
                     ?? throw new InvalidOperationException($"供应商不存在: {sourceId}");

        if (BuiltinProviderCatalog.Find(source.Id) is not null
            && source.Kind != ApiProviderKinds.Custom)
            throw new InvalidOperationException("内置供应商不可复制，请添加自定义供应商");

        var newId = "custom-" + Guid.NewGuid().ToString("N")[..10];
        var copy = new ApiProviderEntry
        {
            Id = newId,
            DisplayName = string.IsNullOrWhiteSpace(newName) ? $"{source.DisplayName} (副本)" : newName.Trim(),
            Kind = ApiProviderKinds.Custom,
            RouteMode = source.RouteMode,
            BaseUrl = source.BaseUrl,
            Enabled = source.Enabled,
            Models = source.Models.ToList(),
            ModelMappings = source.ModelMappings.Select(m => new ModelMappingEntry
            {
                RequestModel = m.RequestModel,
                ActualModel = m.ActualModel
            }).ToList(),
            DefaultForAgent = false,
            DefaultForChat = false
        };

        var apiKey = ResolveApiKey(source);
        if (!string.IsNullOrWhiteSpace(apiKey))
            CredentialVault.Set(newId, "api_key", apiKey);

        AddOrUpdate(config, copy);
        return copy;
    }

    private static List<ApiProviderEntry> LoadFromFile()
    {
        if (!File.Exists(ProvidersFilePath))
            return new List<ApiProviderEntry>();
        try
        {
            var json = File.ReadAllText(ProvidersFilePath);
            return JsonSerializer.Deserialize<List<ApiProviderEntry>>(json, JsonOptions) ?? new();
        }
        catch
        {
            return new List<ApiProviderEntry>();
        }
    }

    private static IReadOnlyList<ApiProviderEntry> Merge(AppConfig config, List<ApiProviderEntry> disk)
    {
        if (config.ApiProviders is not { Count: > 0 })
            return disk;
        var map = disk.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var p in config.ApiProviders)
            map[p.Id] = p;
        return map.Values.ToList();
    }
}
