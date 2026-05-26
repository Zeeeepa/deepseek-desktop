using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>供应商在线状态与启用开关：须有至少一个有效账户才算在线/可启用。</summary>
public static class ProviderAvailabilitySync
{
    /// <summary>将无可用账户但仍标记为启用的供应商写回为禁用。</summary>
    public static void NormalizeEnabledFlags(AppConfig config)
    {
        var changed = false;
        foreach (var entry in ApiProviderRegistry.LoadAll(config))
        {
            if (!entry.Enabled || HasUsableAccount(entry, config))
                continue;
            entry.Enabled = false;
            ApiProviderRegistry.AddOrUpdate(config, entry);
            changed = true;
        }

        _ = changed;
    }

    public static bool HasUsableAccount(ApiProviderEntry entry, AppConfig config) =>
        ProviderAccountStore.ByProvider(entry.Id).Any(a => AccountHasCredentials(entry, a, config));

    /// <summary>供应商列表仅展示至少有一个账户的项（账户在线/离线均可）。</summary>
    public static bool HasAnyAccount(string providerId) =>
        ProviderAccountStore.ByProvider(providerId).Count > 0;

    public static IEnumerable<ApiProviderEntry> ListProvidersWithAccounts(AppConfig config) =>
        ApiProviderRegistry.LoadAll(config).Where(p => HasAnyAccount(p.Id));

    /// <summary>从 api-providers.json 移除无任何账户的供应商（含内置）。</summary>
    public static void PruneProvidersWithoutAccounts(AppConfig config)
    {
        if (!File.Exists(ApiProviderRegistry.ProvidersFilePath))
            return;

        var removed = false;
        foreach (var entry in ApiProviderRegistry.LoadAll(config).ToList())
        {
            if (HasAnyAccount(entry.Id))
                continue;

            if (ApiProviderRegistry.Delete(entry.Id))
                removed = true;
        }

        _ = removed;
    }

    /// <summary>添加首个账户时确保内置供应商在注册表中存在。</summary>
    public static bool EnsureProviderRegistryEntry(AppConfig config, string providerId)
    {
        if (ApiProviderRegistry.Get(config, providerId) is not null)
            return false;

        var builtin = BuiltinProviderCatalog.Find(providerId);
        if (builtin is null)
            return false;

        var isDeepSeek = string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase);
        ApiProviderRegistry.AddOrUpdate(config, new ApiProviderEntry
        {
            Id = builtin.Id,
            DisplayName = builtin.Name,
            Kind = isDeepSeek ? ApiProviderKinds.BuiltinWeb : ApiProviderKinds.Custom,
            RouteMode = isDeepSeek ? ApiRouteModes.EmbeddedWeb : ApiRouteModes.DirectApi,
            BaseUrl = builtin.ApiEndpoint,
            Enabled = true,
            Models = builtin.Models.ToList()
        });
        return true;
    }

    public static bool AccountHasCredentials(
        ApiProviderEntry entry,
        ProviderAccountRecord account,
        AppConfig config)
    {
        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(entry.Id, "deepseek", StringComparison.OrdinalIgnoreCase)
            || entry.Kind == ApiProviderKinds.BuiltinWeb)
            return !string.IsNullOrWhiteSpace(AccountCredentials.ResolveWebUserToken(account, config));

        return account.Credentials.Values.Any(v =>
            !string.IsNullOrWhiteSpace(v) && !v.StartsWith("••••", StringComparison.Ordinal));
    }

    /// <summary>删除最后一个账户后：从注册表移除该供应商（无账户则不保留）。</summary>
    public static void OnLastAccountRemoved(AppConfig config, string providerId)
    {
        ApiProviderRegistry.Delete(providerId);
    }

    public static void EnsureCanEnable(AppConfig config, ApiProviderEntry entry)
    {
        if (HasUsableAccount(entry, config))
            return;

        throw new InvalidOperationException("请先添加至少一个有效账户后再启用该供应商");
    }
}
