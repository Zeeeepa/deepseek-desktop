using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>供应商模型列表与同步（对齐 Chat2API storeManager.getEffectiveModels / providers:*Models）。</summary>
public static class ProviderModelCatalog
{
    public sealed class SyncModelsResult
    {
        public bool Success { get; init; }
        public string[]? SupportedModels { get; init; }
        public Dictionary<string, string>? ModelMappings { get; init; }
        public string? Error { get; init; }
        public int ModelsCount => SupportedModels?.Length ?? 0;
    }

    public static object[] GetEffectiveModels(AppConfig config, string? providerId)
    {
        providerId = string.IsNullOrWhiteSpace(providerId) ? "deepseek" : providerId;
        return DsdUserModelOverridesStore.GetEffectiveModels(config, providerId);
    }

    public static async Task<SyncModelsResult> SyncModelsAsync(
        AppConfig config,
        string providerId,
        CancellationToken ct = default)
    {
        try
        {
            ProviderModelFetcher.FetchedModels fetched;
            var builtin = BuiltinProviderCatalog.Find(providerId);
            if (!string.IsNullOrWhiteSpace(builtin?.ModelsApiEndpoint))
                fetched = await ProviderModelFetcher.FetchBuiltinModelsAsync(providerId, ct);
            else
                fetched = ProviderModelFetcher.FromStaticBuiltin(providerId, config);

            ProviderModelFetcher.ApplyToProvider(config, providerId, fetched);
            return new SyncModelsResult
            {
                Success = true,
                SupportedModels = fetched.SupportedModels,
                ModelMappings = fetched.ModelMappings
            };
        }
        catch (Exception ex)
        {
            return new SyncModelsResult { Success = false, Error = ex.Message };
        }
    }

    public static async Task<SyncModelsResult> UpdateModelsAsync(
        AppConfig config,
        string providerId,
        CancellationToken ct = default)
    {
        try
        {
            var fetched = await ProviderModelFetcher.FetchWithAccountAsync(providerId, ct);
            ProviderModelFetcher.ApplyToProvider(config, providerId, fetched);
            return new SyncModelsResult
            {
                Success = true,
                SupportedModels = fetched.SupportedModels,
                ModelMappings = fetched.ModelMappings
            };
        }
        catch (Exception ex)
        {
            return new SyncModelsResult { Success = false, Error = ex.Message };
        }
    }

}
