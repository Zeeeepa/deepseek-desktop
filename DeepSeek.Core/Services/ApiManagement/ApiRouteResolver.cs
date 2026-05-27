using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services.ApiManagement;

public sealed class ApiRouteResolution
{
    public required ApiProviderEntry Provider { get; init; }
    public ProviderAccountRecord? Account { get; init; }
    public string? ResolvedModel { get; init; }
    public required IApiProviderAdapter Adapter { get; init; }
    public required IAgentWebChat ChatClient { get; init; }
    public string RouteMode => Adapter.RouteMode;
}

public static class ApiRouteResolver
{
    public static ApiRouteResolution Resolve(
        AppConfig config,
        IAgentWebChat? webBridge,
        string? providerId = null,
        string? model = null)
    {
        var requestedModel = string.IsNullOrWhiteSpace(model) ? config.Model : model;
        var (preferredProviderId, preferredAccountId) = ResolveMappingPreferences(config, requestedModel);
        if (!string.IsNullOrWhiteSpace(providerId))
            preferredProviderId = providerId;

        var selection = AccountLoadBalancer.Instance.SelectAccount(
            config,
            requestedModel,
            preferredProviderId,
            preferredAccountId);

        ApiProviderEntry provider;
        ProviderAccountRecord? account;
        string resolvedModel;

        if (selection is not null)
        {
            provider = selection.Provider;
            account = selection.Account;
            resolvedModel = selection.ActualModel;
        }
        else
        {
            provider = ResolveProvider(config, providerId, requestedModel)
                       ?? ApiProviderRegistry.CreateDefaultDeepSeek(config);
            account = SelectFallbackAccount(provider.Id, config);
            resolvedModel = DsdOpenAiCompat.MapModel(requestedModel, config);
        }

        var adapter = CreateAdapter(provider);
        return new ApiRouteResolution
        {
            Provider = provider,
            Account = account,
            ResolvedModel = resolvedModel,
            Adapter = adapter,
            ChatClient = adapter.CreateChatClient(config, webBridge, account)
        };
    }

    private static (string? PreferredProviderId, string? PreferredAccountId) ResolveMappingPreferences(
        AppConfig config,
        string model)
    {
        var mapping = config.ModelMappings.FirstOrDefault(x =>
            x.RequestModel.Equals(model, StringComparison.OrdinalIgnoreCase));
        if (mapping is null)
            return (null, null);
        return (mapping.PreferredProviderId, mapping.PreferredAccountId);
    }

    public static ApiProviderEntry? ResolveProviderForModel(AppConfig config, string? model)
    {
        foreach (var p in ApiProviderRegistry.LoadAll(config).Where(x => x.Enabled))
        {
            if (p.ModelMappings.Any(m =>
                    string.Equals(m.RequestModel, model, StringComparison.OrdinalIgnoreCase)))
                return p;
            if (p.Models.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase)))
                return p;
        }

        return ApiProviderRegistry.Get(config, config.AgentDefaultProviderId);
    }

    public static bool UsesEmbeddedWeb(AppConfig config, string? providerId = null)
    {
        var p = ApiProviderRegistry.Get(config, providerId);
        return p is null || p.RouteMode == ApiRouteModes.EmbeddedWeb;
    }

    private static ApiProviderEntry? ResolveProvider(AppConfig config, string? providerId, string? model)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
            return ApiProviderRegistry.Get(config, providerId);
        if (!string.IsNullOrWhiteSpace(model))
            return ResolveProviderForModel(config, model);
        return ApiProviderRegistry.Get(config, config.AgentDefaultProviderId);
    }

    public static IApiProviderAdapter CreateAdapterForEntry(ApiProviderEntry provider) =>
        CreateAdapter(provider);

    private static IApiProviderAdapter CreateAdapter(ApiProviderEntry provider) =>
        provider.Kind switch
        {
            ApiProviderKinds.BuiltinWeb => new DeepSeekWebBridgeAdapter(provider),
            ApiProviderKinds.Sidecar => new ProviderSidecarAdapter(provider),
            _ => new OpenAiCompatibleAdapter(provider)
        };

    private static ProviderAccountRecord? SelectFallbackAccount(string providerId, AppConfig config) =>
        ProviderAccountStore.ByProvider(providerId)
            .FirstOrDefault(a =>
                string.Equals(a.Status, "active", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(
                    AccountCredentials.ResolveWebUserToken(a, config, allowConfigFallback: false)));
}
