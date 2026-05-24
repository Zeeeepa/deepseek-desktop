namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessTrace
{
    public void Turn(int n, string note) =>
        AgentDebugLogger.Current?.Write("HARNESS", $"turn={n} {note}");

    public void Tool(string name, long ms, bool error) =>
        AgentDebugLogger.Current?.Write("HARNESS", $"tool={name} ms={ms} err={error}");

    public void Sandbox(string action, string detail) =>
        AgentDebugLogger.Current?.Write("HARNESS", $"sandbox={action} {detail}");

    public void PathMap(string virtualPath, string physicalPath) =>
        AgentDebugLogger.Current?.Write("HARNESS", $"pathmap {virtualPath} -> {physicalPath}");
}
