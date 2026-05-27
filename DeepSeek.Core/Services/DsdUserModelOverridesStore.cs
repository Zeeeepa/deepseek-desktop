using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>用户模型覆盖（对齐 Chat2API userModelOverrides / addCustomModel / excludedModels）。</summary>
public static class DsdUserModelOverridesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly object Gate = new();

    public sealed class CustomModelRecord
    {
        public string DisplayName { get; set; } = "";
        public string ActualModelId { get; set; } = "";
    }

    public sealed class ProviderOverrideRecord
    {
        public List<CustomModelRecord> AddedModels { get; set; } = new();
        public List<string> ExcludedModels { get; set; } = new();
    }

    private static string FilePath =>
        Path.Combine(ConfigStore.ConfigDirectory, "user-model-overrides.json");

    public static ProviderOverrideRecord GetProviderOverrides(string providerId)
    {
        lock (Gate)
        {
            var all = Load();
            return all.TryGetValue(providerId, out var o)
                ? o
                : new ProviderOverrideRecord();
        }
    }

    public static object[] GetEffectiveModels(AppConfig config, string providerId)
    {
        var supported = GetProviderSupportedModels(config, providerId);
        var mappingDict = BuildMappingDict(config, providerId);
        var overrides = GetProviderOverrides(providerId);
        var list = new List<object>();

        foreach (var displayName in supported)
        {
            if (overrides.ExcludedModels.Contains(displayName, StringComparer.OrdinalIgnoreCase))
                continue;
            list.Add(new
            {
                displayName,
                actualModelId = mappingDict.TryGetValue(displayName, out var actual) ? actual : displayName,
                isCustom = false
            });
        }

        foreach (var custom in overrides.AddedModels)
        {
            list.Add(new
            {
                displayName = custom.DisplayName,
                actualModelId = custom.ActualModelId,
                isCustom = true
            });
        }

        return list.ToArray();
    }

    public static object[] AddCustomModel(string providerId, CustomModelRecord model)
    {
        lock (Gate)
        {
            var all = Load();
            if (!all.TryGetValue(providerId, out var o))
            {
                o = new ProviderOverrideRecord();
                all[providerId] = o;
            }

            if (o.AddedModels.Any(m =>
                    string.Equals(m.DisplayName, model.DisplayName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(m.ActualModelId, model.ActualModelId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Model with display name \"{model.DisplayName}\" or actual ID \"{model.ActualModelId}\" already exists");
            }

            o.AddedModels.Add(model);
            Save(all);
        }

        return GetEffectiveModels(ConfigStore.Load(), providerId);
    }

    public static object[] RemoveModel(AppConfig config, string providerId, string modelName)
    {
        var supported = GetProviderSupportedModels(config, providerId);
        lock (Gate)
        {
            var all = Load();
            if (!all.TryGetValue(providerId, out var o))
            {
                o = new ProviderOverrideRecord();
                all[providerId] = o;
            }

            var isDefault = supported.Contains(modelName, StringComparer.OrdinalIgnoreCase);
            if (isDefault)
            {
                if (!o.ExcludedModels.Contains(modelName, StringComparer.OrdinalIgnoreCase))
                    o.ExcludedModels.Add(modelName);
            }
            else
            {
                o.AddedModels.RemoveAll(m =>
                    string.Equals(m.DisplayName, modelName, StringComparison.OrdinalIgnoreCase));
            }

            Save(all);
        }

        return GetEffectiveModels(config, providerId);
    }

    public static object[] ResetProvider(string providerId)
    {
        lock (Gate)
        {
            var all = Load();
            all.Remove(providerId);
            Save(all);
        }

        return GetEffectiveModels(ConfigStore.Load(), providerId);
    }

    public static string[] GetProviderSupportedModels(AppConfig config, string providerId)
    {
        if (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
            return DsdOpenAiCompat.ListModelIds(config).ToArray();

        var entry = ApiProviderRegistry.Get(config, providerId);
        var builtin = BuiltinProviderCatalog.Find(providerId);
        if (entry is not null && entry.Models.Count > 0)
            return entry.Models.ToArray();
        if (builtin is not null)
            return builtin.Models;

        return Array.Empty<string>();
    }

    private static Dictionary<string, string> BuildMappingDict(AppConfig config, string providerId)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = ApiProviderRegistry.Get(config, providerId);
        if (entry is not null)
        {
            foreach (var m in entry.ModelMappings)
                dict[m.RequestModel] = m.ActualModel;
        }

        var builtin = BuiltinProviderCatalog.Find(providerId);
        if (builtin?.DefaultModelMappings is not null)
        {
            foreach (var kv in builtin.DefaultModelMappings)
                dict[kv.Key] = kv.Value;
        }

        foreach (var m in config.ModelMappings)
        {
            if (!string.IsNullOrWhiteSpace(m.PreferredProviderId)
                && !string.Equals(m.PreferredProviderId, providerId, StringComparison.OrdinalIgnoreCase))
                continue;
            dict[m.RequestModel] = m.ActualModel;
        }

        return dict;
    }

    private static Dictionary<string, ProviderOverrideRecord> Load()
    {
        if (!File.Exists(FilePath))
            return new Dictionary<string, ProviderOverrideRecord>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ProviderOverrideRecord>>(File.ReadAllText(FilePath), JsonOptions)
                   ?? new Dictionary<string, ProviderOverrideRecord>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, ProviderOverrideRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(Dictionary<string, ProviderOverrideRecord> all)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, JsonOptions));
    }
}
