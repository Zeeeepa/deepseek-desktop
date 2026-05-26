using System.Net.Http.Headers;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>对齐 Chat2API ProviderChecker.fetchProviderModels / providers:updateModels。</summary>
public static class ProviderModelFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public sealed class FetchedModels
    {
        public string[] SupportedModels { get; init; } = Array.Empty<string>();
        public Dictionary<string, string> ModelMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<FetchedModels> FetchBuiltinModelsAsync(string providerId, CancellationToken ct = default)
    {
        var builtin = BuiltinProviderCatalog.Find(providerId)
                      ?? throw new InvalidOperationException($"Provider {providerId} not found");

        if (string.IsNullOrWhiteSpace(builtin.ModelsApiEndpoint))
            throw new InvalidOperationException($"Provider {providerId} does not support dynamic model fetching");

        return await FetchFromEndpointAsync(builtin.ModelsApiEndpoint, builtin.ModelsApiHeaders, null, null, ct);
    }

    public static async Task<FetchedModels> FetchWithAccountAsync(
        string providerId,
        CancellationToken ct = default)
    {
        var builtin = BuiltinProviderCatalog.Find(providerId);
        if (builtin is null || string.IsNullOrWhiteSpace(builtin.ModelsApiEndpoint))
            throw new InvalidOperationException("This provider does not support dynamic model updates");

        var accounts = ProviderAccountStore.ByProvider(providerId);
        var active = accounts.FirstOrDefault(a =>
            string.Equals(a.Status, "active", StringComparison.OrdinalIgnoreCase));

        string? bearer = null;
        string? cookie = null;
        if (active is not null)
        {
            if (active.Credentials.TryGetValue("token", out var token) && !string.IsNullOrWhiteSpace(token))
                bearer = token;
            if (active.Credentials.TryGetValue("cookies", out var cookies) && !string.IsNullOrWhiteSpace(cookies))
                cookie = cookies;
        }

        return await FetchFromEndpointAsync(builtin.ModelsApiEndpoint, builtin.ModelsApiHeaders, bearer, cookie, ct);
    }

    public static FetchedModels FromStaticBuiltin(string providerId, AppConfig config)
    {
        var builtin = BuiltinProviderCatalog.Find(providerId);
        if (builtin is null)
            throw new InvalidOperationException($"Provider {providerId} not found");

        var models = builtin.Id == "deepseek"
            ? DsdOpenAiCompat.ListModelIds(config).ToArray()
            : builtin.Models;

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in models)
            mappings[m] = m;

        if (builtin.DefaultModelMappings is not null)
        {
            foreach (var kv in builtin.DefaultModelMappings)
                mappings[kv.Key] = kv.Value;
        }

        return new FetchedModels { SupportedModels = models, ModelMappings = mappings };
    }

    private static async Task<FetchedModels> FetchFromEndpointAsync(
        string endpoint,
        IReadOnlyDictionary<string, string>? extraHeaders,
        string? bearerToken,
        string? cookieHeader,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        if (extraHeaders is not null)
        {
            foreach (var kv in extraHeaders)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        if (!string.IsNullOrWhiteSpace(bearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if ((int)resp.StatusCode != 200)
            throw new InvalidOperationException($"Failed to fetch models: HTTP {(int)resp.StatusCode}");

        return ParseModelsJson(body);
    }

    internal static FetchedModels ParseModelsJson(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        JsonElement modelsEl;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            modelsEl = data;
        else if (root.ValueKind == JsonValueKind.Array)
            modelsEl = root;
        else
            throw new InvalidOperationException("No models found in the response");

        var supported = new List<string>();
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var model in modelsEl.EnumerateArray())
        {
            if (model.ValueKind == JsonValueKind.String)
            {
                var name = model.GetString()!;
                supported.Add(name);
                mappings[name] = name;
                continue;
            }

            if (model.ValueKind != JsonValueKind.Object) continue;
            var id = model.TryGetProperty("id", out var idEl) ? idEl.GetString()
                : model.TryGetProperty("model_id", out var mid) ? mid.GetString()
                : model.TryGetProperty("name", out var nm) ? nm.GetString() : null;
            var display = model.TryGetProperty("name", out var nameEl) ? nameEl.GetString()
                : model.TryGetProperty("display_name", out var dn) ? dn.GetString() : id;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var label = string.IsNullOrWhiteSpace(display) ? id! : display!;
            supported.Add(label);
            mappings[label] = id!;
        }

        if (supported.Count == 0)
            throw new InvalidOperationException("Failed to parse models from the response");

        return new FetchedModels
        {
            SupportedModels = supported.ToArray(),
            ModelMappings = mappings
        };
    }

    public static void ApplyToProvider(AppConfig config, string providerId, FetchedModels fetched)
    {
        var entry = ApiProviderRegistry.Get(config, providerId);
        var builtin = BuiltinProviderCatalog.Find(providerId);
        entry ??= new ApiProviderEntry
        {
            Id = providerId,
            DisplayName = builtin?.Name ?? providerId,
            Kind = ApiProviderKinds.Custom,
            RouteMode = ApiRouteModes.DirectApi,
            BaseUrl = builtin?.ApiEndpoint ?? "",
            Enabled = false
        };

        entry.Models = fetched.SupportedModels.ToList();
        entry.ModelMappings = fetched.ModelMappings.Select(kv => new ModelMappingEntry
        {
            RequestModel = kv.Key,
            ActualModel = kv.Value,
            PreferredProviderId = providerId
        }).ToList();
        ApiProviderRegistry.AddOrUpdate(config, entry);
    }
}
