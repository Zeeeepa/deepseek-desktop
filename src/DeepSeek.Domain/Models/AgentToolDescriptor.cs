namespace DeepSeekBrowser.Models;

public sealed class AgentToolDescriptor
{
    public string ExposedName { get; init; } = "";
    public string Description { get; init; } = "";
    public string ParametersJson { get; init; } = "{}";
}
