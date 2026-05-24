using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DeepSeekBrowser.Services;

public sealed class McpHub : IAsyncDisposable
{
    private readonly Dictionary<string, ConnectedServer> _servers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private sealed class ConnectedServer
    {
        public required McpServerConfig Config { get; init; }
        public required McpClient Client { get; init; }
        public required Dictionary<string, string> ToolToOriginalName { get; init; }
        public List<AgentToolDescriptor> ToolDescriptors { get; init; } = [];
    }

    public int ConnectedCount => _servers.Count;

    public bool IsConnected(string serverId) => _servers.ContainsKey(serverId);

    public int GetToolCount(string serverId) =>
        _servers.TryGetValue(serverId, out var entry) ? entry.ToolToOriginalName.Count : 0;

    public async Task<IReadOnlyList<string>> ListToolNamesAsync(string serverId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_servers.TryGetValue(serverId, out var entry))
                return Array.Empty<string>();

            if (entry.Client.ServerCapabilities.Tools is null)
                return Array.Empty<string>();

            var tools = await entry.Client.ListToolsAsync(cancellationToken: ct);
            return tools.Select(t => t.Name).OrderBy(n => n).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ConnectEnabledAsync(
        IEnumerable<McpServerConfig> servers,
        Action<string>? log,
        CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var server in servers.Where(s => s.Enabled))
        {
            try
            {
                using var perServer = CancellationTokenSource.CreateLinkedTokenSource(ct);
                perServer.CancelAfter(TimeSpan.FromSeconds(20));
                await ConnectAsync(server, log, perServer.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                errors.Add($"{server.Name}: 连接超时（20s）");
            }
            catch (Exception ex)
            {
                errors.Add($"{server.Name}: {ex.Message}");
            }
        }

        return errors;
    }

    private static async Task<McpClient> ConnectRemoteWithRetryAsync(
        string name,
        Uri endpoint,
        Action<string>? log,
        CancellationToken ct)
    {
        const int maxAttempts = 5;
        Exception? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Name = name,
                    Endpoint = endpoint,
                    TransportMode = HttpTransportMode.StreamableHttp
                });
                return await McpClient.CreateAsync(transport, cancellationToken: ct);
            }
            catch (Exception ex) when (attempt < maxAttempts && McpRemoteEndpoint.IsTransientConnectFailure(ex))
            {
                last = ex;
                var wait = TimeSpan.FromMilliseconds(400 * attempt);
                log?.Invoke($"Unity MCP 尚未就绪，{wait.TotalSeconds:0.#}s 后重试 ({attempt}/{maxAttempts})…");
                await Task.Delay(wait, ct);
            }
        }

        throw last ?? new InvalidOperationException($"无法连接远程 MCP: {endpoint}");
    }

    public async Task ConnectAsync(McpServerConfig config, Action<string>? log, CancellationToken ct)
    {
        ConnectedServer? replaced = null;
        await _gate.WaitAsync(ct);
        try
        {
            if (_servers.Remove(config.Id, out var existing))
                replaced = existing;
        }
        finally
        {
            _gate.Release();
        }

        if (replaced is not null)
            await replaced.Client.DisposeAsync();

        log?.Invoke($"正在连接 MCP: {config.Name}…");

        McpClient client;
        if (config.IsRemote)
        {
            if (string.IsNullOrWhiteSpace(config.Url))
                throw new InvalidOperationException($"远程 MCP「{config.Name}」未填写 URL。");

            var endpoint = McpRemoteEndpoint.Resolve(config.Url);
            if (!string.Equals(config.Url.Trim().TrimEnd('/'), endpoint.ToString().TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                config.Url = endpoint.ToString();
                log?.Invoke($"已补全 MCP 路径: {endpoint}");
            }

            client = await ConnectRemoteWithRetryAsync(config.Name, endpoint, log, ct);
        }
        else
        {
            var env = config.Environment.ToDictionary(
                kv => kv.Key,
                kv => (string?)kv.Value,
                StringComparer.OrdinalIgnoreCase);

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = config.Name,
                Command = config.Command,
                Arguments = config.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory)
                    ? null
                    : config.WorkingDirectory,
                EnvironmentVariables = env
            });
            client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        }

        var toolMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptors = new List<AgentToolDescriptor>();

        if (client.ServerCapabilities.Tools is not null)
        {
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            foreach (var tool in tools)
            {
                var exposed = $"{config.Id}__{tool.Name}";
                toolMap[exposed] = tool.Name;
                descriptors.Add(CreateDescriptor(config, exposed, tool.Name, tool.Description, tool.JsonSchema.GetRawText()));
            }
        }

        var entry = new ConnectedServer
        {
            Config = config,
            Client = client,
            ToolToOriginalName = toolMap,
            ToolDescriptors = descriptors
        };

        await _gate.WaitAsync(ct);
        try
        {
            if (_servers.Remove(config.Id, out var raced))
            {
                await raced.Client.DisposeAsync();
            }

            _servers[config.Id] = entry;
            log?.Invoke($"已连接 {config.Name}，工具数: {toolMap.Count}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(string serverId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await DisconnectCoreAsync(serverId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DisconnectCoreAsync(string serverId)
    {
        if (!_servers.Remove(serverId, out var entry))
            return;

        await entry.Client.DisposeAsync();
    }

    public async Task DisconnectAllAsync(CancellationToken ct)
    {
        var ids = _servers.Keys.ToList();
        foreach (var id in ids)
            await DisconnectAsync(id, ct);
    }

    public async Task<IReadOnlyList<AgentToolDescriptor>> ListAllToolsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _servers.Values.SelectMany(s => s.ToolDescriptors).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public string ResolveExposedToolName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return rawName;

        if (rawName.Contains("__", StringComparison.Ordinal))
            return rawName;

        foreach (var server in _servers.Values)
        {
            foreach (var exposed in server.ToolToOriginalName.Keys)
            {
                if (exposed.Equals(rawName, StringComparison.OrdinalIgnoreCase)
                    || exposed.EndsWith("__" + rawName, StringComparison.OrdinalIgnoreCase))
                    return exposed;
            }
        }

        return rawName;
    }

    public async Task<string> BuildToolCatalogTextAsync(CancellationToken ct)
    {
        var tools = await ListAllToolsAsync(ct);
        if (tools.Count == 0)
            return "已连接的 MCP 工具（调用名须完全一致）：\n（无）";

        var sb = new StringBuilder("已连接的 MCP 工具（调用名须完全一致）：\n");
        foreach (var t in tools)
            sb.Append("- ").Append(t.ExposedName).AppendLine(t.Description.Length > 0 ? $" — {t.Description}" : "");
        return sb.ToString().TrimEnd();
    }

    public async Task<List<object>> GetOpenAiToolsAsync(CancellationToken ct)
    {
        var descriptors = await ListAllToolsAsync(ct);
        return descriptors.Select(t => (object)new
        {
            type = "function",
            function = new
            {
                name = t.ExposedName,
                description = t.Description,
                parameters = ParseInputSchema(t.ParametersJson)
            }
        }).ToList();
    }

    private static AgentToolDescriptor CreateDescriptor(
        McpServerConfig config, string exposed, string toolName, string? description, string schemaJson) =>
        new()
        {
            ExposedName = exposed,
            Description = string.IsNullOrWhiteSpace(description)
                ? $"[{config.Name}] {toolName}"
                : $"[{config.Name}] {description}",
            ParametersJson = schemaJson
        };

    public async Task<string> CallToolAsync(string exposedName, string argumentsJson, CancellationToken ct)
    {
        var sep = exposedName.IndexOf("__", StringComparison.Ordinal);
        if (sep <= 0)
            throw new InvalidOperationException($"无效工具名: {exposedName}");

        var serverId = exposedName[..sep];
        var toolName = exposedName[(sep + 2)..];

        await _gate.WaitAsync(ct);
        try
        {
            if (!_servers.TryGetValue(serverId, out var server))
                throw new InvalidOperationException($"MCP 服务器未连接: {serverId}");

            var args = string.IsNullOrWhiteSpace(argumentsJson)
                ? new Dictionary<string, object?>()
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson)
                  ?? new Dictionary<string, object?>();

            var result = await server.Client.CallToolAsync(toolName, args, cancellationToken: ct);
            return ExtractToolResultText(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static object ParseInputSchema(string schemaJson)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(schemaJson);
        }
        catch
        {
            return new { type = "object", properties = new { } };
        }
    }

    private static string ExtractToolResultText(CallToolResult result)
    {
        var sb = new StringBuilder();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text)
                sb.AppendLine(text.Text);
        }

        if (result.IsError == true)
            return "ERROR: " + sb.ToString().Trim();

        return sb.ToString().Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAllAsync(CancellationToken.None);
        _gate.Dispose();
    }
}
