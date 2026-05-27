namespace DeepSeekBrowser.Services;

/// <summary>
/// Coordinates embedded iframe panels inside the Agent WebView (load state, visibility messages).
/// Business rules live in <see cref="AppLayer.Agent.AgentSessionOrchestrator"/>.
/// </summary>
public sealed class AgentWebViewCoordinator
{
    public event Action<string>? PanelVisibilityChanged;

    public void NotifyPanelVisible(string panelId) => PanelVisibilityChanged?.Invoke(panelId);

    public void NotifyPanelHidden() => PanelVisibilityChanged?.Invoke("");
}
