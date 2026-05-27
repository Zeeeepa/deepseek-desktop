using System.Text.Json.Serialization;

namespace DeepSeekBrowser.Models;

public sealed class McpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "MCP Server";
    public bool Enabled { get; set; } = true;

    /// <summary>stdio = 本地进程；remote = HTTP（与 OpenCode 的 type: remote 一致）</summary>
    [JsonPropertyName("type")]
    public string TransportType { get; set; } = "stdio";

    public string? Url { get; set; }

    public string Command { get; set; } = "npx";
    public List<string> Arguments { get; set; } = ["-y", "@modelcontextprotocol/server-filesystem"];
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();

    [JsonIgnore]
    public bool IsRemote =>
        TransportType.Equals("remote", StringComparison.OrdinalIgnoreCase)
        || TransportType.Equals("http", StringComparison.OrdinalIgnoreCase);

    public string DisplayEndpoint =>
        IsRemote
            ? (Url ?? "")
            : string.IsNullOrWhiteSpace(Command)
                ? ""
                : Arguments.Count > 0
                    ? $"{Command} {Arguments[0]}"
                    : Command;
}
