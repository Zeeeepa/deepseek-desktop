using System.IO;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.DeepSeekTui;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 桌面端 Chat2API 供应商快照：网页 User Token → 本地 OpenAI 兼容 API → DeepSeek-TUI。
/// 对齐 Chat2API 文档、DeepSeek 官方 API 模型名与 deepseek-tui.com 配置方式。
/// </summary>
public static class Chat2ApiProviderService
{
    public sealed record ProviderSnapshot(
        string Id,
        string Name,
        string Type,
        string Description,
        bool Online,
        bool Enabled,
        string AuthType,
        int AccountOnline,
        int AccountTotal,
        int ModelCount,
        string Chat2ApiBaseUrl,
        string ApiKeyForClients,
        string ApiKeyMasked,
        string TuiRuntimeUrl,
        string TuiConfigPath,
        string IntegrationFilePath);

    public static ProviderSnapshot Build(AppConfig config, Chat2ApiHealth? health = null)
    {
        Chat2ApiCompat.EnsureDefaultMappings(config);
        var port = config.LocalApiPort > 0 ? config.LocalApiPort : 5111;
        var baseUrl = $"http://127.0.0.1:{port}/v1";
        var loggedIn = !string.IsNullOrWhiteSpace(config.WebUserToken);
        var online = loggedIn && (health?.CanChat ?? loggedIn);
        var apiKey = ResolveApiKeyForClients(config);
        var tuiPort = config.DeepSeekTuiRuntimePort > 0 ? config.DeepSeekTuiRuntimePort : DeepSeekTuiHost.DefaultPort;

        return new ProviderSnapshot(
            Id: "deepseek-web",
            Name: "DeepSeek",
            Type: "builtin",
            Description: "DeepSeek 智能对话助手，经本地 Chat2API 使用网页登录会话，支持深度思考与联网搜索",
            Online: online,
            Enabled: online,
            AuthType: "user_token",
            AccountOnline: loggedIn ? 1 : 0,
            AccountTotal: 1,
            ModelCount: Chat2ApiCompat.ListModels(config).Count,
            Chat2ApiBaseUrl: baseUrl,
            ApiKeyForClients: apiKey,
            ApiKeyMasked: MaskCredential(apiKey),
            TuiRuntimeUrl: $"http://127.0.0.1:{tuiPort}",
            TuiConfigPath: DeepSeekTuiConfigSync.ConfigPath,
            IntegrationFilePath: IntegrationFilePath);
    }

    /// <summary>供 DeepSeek-TUI / OpenAI 兼容客户端使用的 Bearer Token（网页 User Token 或本地 Key）。</summary>
    public static string ResolveApiKeyForClients(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.WebUserToken))
            return config.WebUserToken.Trim();

        if (config.EnableLocalApiKeyAuth)
        {
            var key = config.LocalApiKeys.FirstOrDefault(k => k.Enabled);
            if (key is not null && !string.IsNullOrWhiteSpace(key.Key))
                return key.Key;
        }

        return "edge-local";
    }

    public static string IntegrationFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepSeekEdge",
            "chat2api-tui-integration.json");

    public static void WriteIntegrationFile(AppConfig config, Chat2ApiHealth? health = null)
    {
        var snap = Build(config, health);
        var dir = Path.GetDirectoryName(snap.IntegrationFilePath)!;
        Directory.CreateDirectory(dir);

        var payload = new
        {
            updated_at = DateTimeOffset.UtcNow.ToString("o"),
            provider = new
            {
                snap.Id,
                snap.Name,
                snap.Type,
                snap.Online,
                snap.AuthType,
                snap.ModelCount
            },
            chat2api = new
            {
                base_url = snap.Chat2ApiBaseUrl,
                api_key = snap.ApiKeyForClients,
                auth_header = $"Bearer {snap.ApiKeyForClients}",
                docs = new[]
                {
                    "https://github.com/xiaoY233/Chat2API",
                    "https://api-docs.deepseek.com/zh-cn/"
                }
            },
            deepseek_tui = new
            {
                runtime_url = snap.TuiRuntimeUrl,
                serve_http = $"{snap.TuiRuntimeUrl} (deepseek serve --http)",
                config_path = snap.TuiConfigPath,
                env = new
                {
                    DEEPSEEK_BASE_URL = snap.Chat2ApiBaseUrl,
                    DEEPSEEK_API_KEY = snap.ApiKeyForClients,
                    DEEPSEEK_MODEL = "deepseek-v4-pro"
                },
                docs = "https://deepseek-tui.com/zh"
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(snap.IntegrationFilePath, json);
    }

    public static object ToApiPayload(ProviderSnapshot snap) => new
    {
        providers = new[]
        {
            new
            {
                id = snap.Id,
                name = snap.Name,
                type = snap.Type,
                description = snap.Description,
                online = snap.Online,
                enabled = snap.Enabled,
                auth_type = snap.AuthType,
                account_online = snap.AccountOnline,
                account_total = snap.AccountTotal,
                model_count = snap.ModelCount,
                base_url = snap.Chat2ApiBaseUrl,
                api_key_masked = snap.ApiKeyMasked
            }
        },
        stats = new
        {
            total = 1,
            online = snap.Online ? 1 : 0,
            enabled = snap.Enabled ? 1 : 0,
            builtin = 1,
            custom = 0
        },
        deepseek_tui = new
        {
            runtime_url = snap.TuiRuntimeUrl,
            config_path = snap.TuiConfigPath,
            integration_file = snap.IntegrationFilePath,
            base_url = snap.Chat2ApiBaseUrl,
            api_key_hint = "使用网页 User Token 作为 DEEPSEEK_API_KEY（已自动写入 ~/.deepseek/config.toml）"
        }
    };

    public static string MaskCredential(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        if (value.Length <= 12) return value[..3] + "…";
        return value[..8] + "…" + value[^4..];
    }
}
