using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.Harness;
using DeepSeekBrowser.Views;

namespace DeepSeekBrowser.Services;

/// <summary>Agent 内嵌设置页（settings-embed.html）的后端 IPC。</summary>
internal sealed class EmbeddedSettingsSupport
{
    private readonly McpHub _mcpHub;
    private readonly Func<Window?> _owner;
    private readonly Func<AppConfig> _getConfig;
    private readonly Action<AppConfig> _setConfig;
    private readonly Func<WebInjectService> _agentInject;

    public EmbeddedSettingsSupport(
        McpHub mcpHub,
        Func<Window?> owner,
        Func<AppConfig> getConfig,
        Action<AppConfig> setConfig,
        Func<WebInjectService> agentInject)
    {
        _mcpHub = mcpHub;
        _owner = owner;
        _getConfig = getConfig;
        _setConfig = setConfig;
        _agentInject = agentInject;
    }

    public async Task HandleAsync(JsonElement msg, CancellationToken ct = default)
    {
        if (!msg.TryGetProperty("type", out var typeEl)) return;
        var type = typeEl.GetString();
        try
        {
            switch (type)
            {
                case "settingsLoad":
                    await ReplyAsync(msg, "settingsLoaded", BuildPayload());
                    break;
                case "settingsSave":
                    ApplySave(msg);
                    await ReplyAsync(msg, "settingsSaved", new { ok = true });
                    break;
                case "settingsConnectAllMcp":
                    var summary = await ConnectAllMcpAsync();
                    await ReplyAsync(msg, "settingsConnectAllResult", new { summary });
                    break;
                case "settingsMcpToggle":
                    await ToggleMcpAsync(msg);
                    await ReplyAsync(msg, "settingsMcpToggled", new { ok = true });
                    break;
                case "settingsMcpAdd":
                    await AddMcpOnUiAsync();
                    await ReplyAsync(msg, "settingsMcpAdded", new { ok = true });
                    break;
                case "settingsMcpEdit":
                    await EditMcpOnUiAsync(msg);
                    await ReplyAsync(msg, "settingsMcpEdited", new { ok = true });
                    break;
                case "settingsMcpRemove":
                    await RemoveMcpAsync(msg);
                    await ReplyAsync(msg, "settingsMcpRemoved", new { ok = true });
                    break;
                case "settingsRunDoctor":
                    var doctorText = await RunDoctorAsync();
                    await ReplyAsync(msg, "settingsDoctorResult", new { text = doctorText });
                    break;
                case "settingsOpenDeepSeekHome":
                    OpenDeepSeekHome();
                    break;
                case "settingsOpenDocs":
                    OpenHarnessDocs();
                    break;
                case "settingsCopyConfigPath":
                    Clipboard.SetText(ConfigStore.ConfigFilePath);
                    break;
                case "settingsOpenConfig":
                    OpenConfigFile();
                    break;
            }
        }
        catch (Exception ex)
        {
            await ReplyAsync(msg, type + "Result", new { ok = false, error = ex.Message }, ok: false, error: ex.Message);
        }
    }

    private object BuildPayload()
    {
        var config = _getConfig();
        Chat2ApiCompat.EnsureDefaultMappings(config);
        var mode = string.Equals(config.Chat2ApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase)
            ? "多轮" : "单轮";
        var chat2ApiSummary =
            $"内嵌 Chat2API · {Chat2ApiCompat.DefaultModel} · 会话 {mode} · {config.ModelMappings.Count} 条模型别名" +
            (config.EnableLocalApiKeyAuth ? " · API Key 认证已启用" : "") +
            " · 网页登录后自动同步 Token";

        var serviceSummary =
            "对话桥接与 Agent Harness 已在进程内运行；网页登录 Token 自动同步至 Chat2API。";

        var harnessInfo =
            "DSD Harness（C# 进程内）· Execute / Blueprint 工作流\n" +
            "LLM：网页桥 + Chat2API · 工具：文件/Shell + MCP\n" +
            $"沙盒：本地工作区（懒加载={(config.AgentSandboxLazyInit ? "是" : "否")}）";

        return new
        {
            ok = true,
            loggedIn = !string.IsNullOrWhiteSpace(config.WebUserToken),
            localApiUrl = $"http://127.0.0.1:{config.LocalApiPort}/v1",
            localApiPort = config.LocalApiPort,
            maxAgentSteps = config.MaxAgentSteps,
            maxSubAgentSteps = config.MaxSubAgentSteps,
            defaultAgentStrategy = config.DefaultAgentStrategy,
            agentSandboxLazyInit = config.AgentSandboxLazyInit,
            chat2ApiSummary,
            serviceSummary,
            harnessInfo,
            agentWorkspaceRoot = config.AgentWorkspaceRoot ?? "",
            agentAllowShell = config.AgentAllowShell,
            enableAdaptiveOutputEscalation = config.EnableAdaptiveOutputEscalation,
            agentApprovalMode = config.AgentApprovalMode ?? "smart",
            configPath = ConfigStore.ConfigFilePath,
            mcpServers = config.McpServers.Select(BuildMcpDto).ToList()
        };
    }

    private object BuildMcpDto(McpServerConfig server)
    {
        var connected = server.Enabled && _mcpHub.IsConnected(server.Id);
        var transport = server.IsRemote ? "HTTP/SSE（远程）" : "stdio（本机）";
        return new
        {
            id = server.Id,
            name = server.Name,
            enabled = server.Enabled,
            connected,
            toolCount = connected ? _mcpHub.GetToolCount(server.Id) : 0,
            transport,
            endpoint = Truncate(server.DisplayEndpoint, 40)
        };
    }

    private void ApplySave(JsonElement msg)
    {
        var config = _getConfig();
        if (msg.TryGetProperty("localApiPort", out var portEl) && portEl.TryGetInt32(out var port))
            config.LocalApiPort = Math.Clamp(port, 0, 65535);
        if (msg.TryGetProperty("maxAgentSteps", out var stepsEl) && stepsEl.TryGetInt32(out var steps))
            config.MaxAgentSteps = Math.Clamp(steps, 1, 100);
        if (msg.TryGetProperty("maxSubAgentSteps", out var subEl) && subEl.TryGetInt32(out var subSteps))
            config.MaxSubAgentSteps = Math.Clamp(subSteps, 1, 50);
        if (msg.TryGetProperty("defaultAgentStrategy", out var stratEl))
            config.DefaultAgentStrategy = HarnessStrategyResolver.Normalize(stratEl.GetString());
        if (msg.TryGetProperty("agentWorkspaceRoot", out var wsEl))
            config.AgentWorkspaceRoot = wsEl.GetString() ?? "";
        if (msg.TryGetProperty("agentAllowShell", out var shellEl))
            config.AgentAllowShell = shellEl.GetBoolean();
        if (msg.TryGetProperty("enableAdaptiveOutputEscalation", out var adaptEl))
            config.EnableAdaptiveOutputEscalation = adaptEl.GetBoolean();
        if (msg.TryGetProperty("agentApprovalMode", out var apprEl))
        {
            config.AgentApprovalMode = apprEl.GetString() ?? "smart";
            config.AgentAutoApproveReadOnly = config.AgentApprovalMode is "smart" or "readonly";
        }
        if (msg.TryGetProperty("agentSandboxLazyInit", out var sbLazyEl))
            config.AgentSandboxLazyInit = sbLazyEl.GetBoolean();

        _setConfig(config);
        AgentDesktopConfigSync.Apply(config);
        ConfigStore.Save(config);
    }

    private async Task<string> ConnectAllMcpAsync()
    {
        var config = _getConfig();
        await _mcpHub.DisconnectAllAsync(CancellationToken.None);
        var errors = await _mcpHub.ConnectEnabledAsync(config.McpServers, _ => { }, CancellationToken.None);
        var totalTools = config.McpServers.Where(s => s.Enabled).Sum(s => _mcpHub.GetToolCount(s.Id));
        ConfigStore.Save(config);
        var summary = $"已连接 {_mcpHub.ConnectedCount} 个 MCP Server，共发现 {totalTools} 个工具。";
        if (errors.Count > 0)
            summary += "\n\n失败:\n" + string.Join("\n", errors);
        return summary;
    }

    private async Task ToggleMcpAsync(JsonElement msg)
    {
        var id = msg.TryGetProperty("serverId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return;
        var config = _getConfig();
        var server = config.McpServers.FirstOrDefault(s => s.Id == id);
        if (server is null) return;
        server.Enabled = !server.Enabled;
        if (server.Enabled)
        {
            if (!_mcpHub.IsConnected(server.Id))
                await _mcpHub.ConnectAsync(server, _ => { }, CancellationToken.None);
        }
        else
            await _mcpHub.DisconnectAsync(server.Id, CancellationToken.None);
        _setConfig(config);
        ConfigStore.Save(config);
    }

    private Task AddMcpOnUiAsync() => RunOnUiAsync(() =>
    {
        var config = _getConfig();
        var dlg = new McpServerEditorWindow { Owner = _owner() };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            config.McpServers.Add(dlg.Result);
            _setConfig(config);
            ConfigStore.Save(config);
        }
    });

    private Task EditMcpOnUiAsync(JsonElement msg) => RunOnUiAsync(() =>
    {
        var id = msg.TryGetProperty("serverId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return;
        var config = _getConfig();
        var server = config.McpServers.FirstOrDefault(s => s.Id == id);
        if (server is null) return;
        var dlg = new McpServerEditorWindow(server) { Owner = _owner() };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            var idx = config.McpServers.IndexOf(server);
            if (idx >= 0) config.McpServers[idx] = dlg.Result;
            _setConfig(config);
            ConfigStore.Save(config);
        }
    });

    private async Task RemoveMcpAsync(JsonElement msg)
    {
        var id = msg.TryGetProperty("serverId", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return;
        var config = _getConfig();
        var server = config.McpServers.FirstOrDefault(s => s.Id == id);
        if (server is null) return;
        try { await _mcpHub.DisconnectAsync(server.Id, CancellationToken.None); }
        catch { /* ignore */ }
        config.McpServers.Remove(server);
        _setConfig(config);
        ConfigStore.Save(config);
    }

    private async Task<string> RunDoctorAsync()
    {
        var config = _getConfig();
        AgentDesktopConfigSync.Apply(config);
        var loggedIn = !string.IsNullOrWhiteSpace(config.WebUserToken);
        var mcp = config.McpServers.Count(s => s.Enabled);
        var text =
            $"Harness: native (in-process)\n" +
            $"默认工作流: {config.DefaultAgentStrategy}\n" +
            $"网页登录: {(loggedIn ? "是" : "否")}\n" +
            $"MCP 已启用: {mcp}\n" +
            $"沙盒: 本地工作区（懒加载={(config.AgentSandboxLazyInit ? "是" : "否")}）\n" +
            $"工作区: {(string.IsNullOrWhiteSpace(config.AgentWorkspaceRoot) ? "默认" : config.AgentWorkspaceRoot)}\n" +
            $"~/.deepseek: {AgentDesktopConfigSync.HomeDirectory}";
        return text;
    }

    private static void OpenHarnessDocs()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", "HARNESS.md"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "HARNESS.md"))
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com",
            UseShellExecute = true
        });
    }

    private static void OpenDeepSeekHome()
    {
        var dir = AgentDesktopConfigSync.HomeDirectory;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private static void OpenConfigFile()
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        if (!File.Exists(ConfigStore.ConfigFilePath))
            ConfigStore.Save(new AppConfig());
        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigStore.ConfigFilePath,
            UseShellExecute = true
        });
    }

    private Task ReplyAsync(JsonElement msg, string type, object payload, bool ok = true, string? error = null)
    {
        var reqId = msg.TryGetProperty("reqId", out var reqEl) ? reqEl.GetString() : null;
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["reqId"] = reqId,
            ["ok"] = ok
        };
        if (error is not null)
            envelope["error"] = error;
        foreach (var prop in doc.RootElement.EnumerateObject())
            envelope[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        return _agentInject().PostToPageAsync(envelope);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "—";
        return text.Length <= max ? text : text[..max] + "…";
    }
}
