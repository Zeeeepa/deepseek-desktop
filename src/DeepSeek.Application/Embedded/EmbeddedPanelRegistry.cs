namespace DeepSeekBrowser.AppLayer.Embedded;

/// <summary>Embedded iframe panel identifiers for Agent shell.</summary>
public static class EmbeddedPanelIds
{
    public const string ApiManagement = "apiManagement";
    public const string Settings = "settings";
    public const string Automations = "automations";
}

/// <summary>Resolves virtual URLs for embedded Agent panels (same-origin dsd-api).</summary>
public sealed class EmbeddedPanelRegistry
{
    public bool TryGetPanel(string panelId, out EmbeddedPanelDescriptor descriptor)
    {
        descriptor = panelId switch
        {
            EmbeddedPanelIds.ApiManagement => new EmbeddedPanelDescriptor(
                EmbeddedPanelIds.ApiManagement,
                "#/providers",
                "API 管理"),
            EmbeddedPanelIds.Settings => new EmbeddedPanelDescriptor(
                EmbeddedPanelIds.Settings,
                "#/settings",
                "设置"),
            EmbeddedPanelIds.Automations => new EmbeddedPanelDescriptor(
                EmbeddedPanelIds.Automations,
                "#/automations",
                "自动化"),
            _ => default,
        };

        return descriptor.IsValid;
    }

    public string BuildUrl(string agentOrigin, string panelId)
    {
        if (!TryGetPanel(panelId, out var panel))
            throw new ArgumentException($"Unknown embedded panel: {panelId}", nameof(panelId));

        var baseUrl = agentOrigin.TrimEnd('/');
        return $"{baseUrl}/dsd-api/index.html{panel.HashRoute}";
    }
}

public readonly struct EmbeddedPanelDescriptor
{
    public EmbeddedPanelDescriptor(string id, string hashRoute, string title)
    {
        Id = id;
        HashRoute = hashRoute;
        Title = title;
    }

    public string Id { get; }
    public string HashRoute { get; }
    public string Title { get; }
    public bool IsValid => !string.IsNullOrEmpty(Id);
}
