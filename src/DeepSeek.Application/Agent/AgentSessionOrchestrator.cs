using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.AppLayer.Agent;

/// <summary>
/// Agent-side session orchestration (model routing, provider catalog shaping).
/// UI/WebView coordination stays in Desktop <see cref="Services.AgentWebViewCoordinator"/>.
/// </summary>
public sealed class AgentSessionOrchestrator
{
    public AgentProviderCatalogSnapshot BuildProviderCatalog(AppConfig config, IReadOnlyList<AgentProviderCatalogEntry> providers)
    {
        var order = config.AgentAutoProviderOrder ?? [];
        var ordered = providers
            .OrderBy(p => order.IndexOf(p.Id) is >= 0 and var idx ? idx : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AgentProviderCatalogSnapshot
        {
            Providers = ordered,
            AutoPreferProviderOrder = config.AgentAutoPreferProviderOrder,
            AutoProviderOrder = order,
        };
    }

    public string ResolveDisplayModel(AppConfig config) =>
        config.AgentModelAuto
            ? "Auto"
            : (string.IsNullOrWhiteSpace(config.AgentManualModel) ? "deepseek-v4-pro" : config.AgentManualModel);
}

public sealed class AgentProviderCatalogSnapshot
{
    public required IReadOnlyList<AgentProviderCatalogEntry> Providers { get; init; }
    public bool AutoPreferProviderOrder { get; init; }
    public IReadOnlyList<string> AutoProviderOrder { get; init; } = Array.Empty<string>();
}

public sealed class AgentProviderCatalogEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; }
    public bool HasActiveAccount { get; init; }
}
