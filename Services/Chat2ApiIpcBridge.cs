using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// WebView2 版 Chat2API 管理台 IPC 桥：将 <c>window.electronAPI</c> 调用映射到内嵌 Chat2API 栈。
/// </summary>
public sealed class Chat2ApiIpcBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly LocalOpenAiServer _localApi;
    private readonly WebInjectService _web;
    private readonly Func<Window?> _owner;
    private readonly Func<AppConfig, CancellationToken, Task>? _onStackSync;
    private AppConfig _config;
    private readonly long _proxyStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private readonly Dictionary<string, JsonElement> _uiStore = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (object Value, long ExpiresAt)> _readCache = new(StringComparer.Ordinal);

    private static readonly HashSet<string> CachedReadChannels = new(StringComparer.Ordinal)
    {
        "proxy:getStatus",
        "proxy:getStatistics",
        "config:get",
        "providers:getAll",
        "providers:getBuiltin",
        "providers:checkAllStatus",
        "statistics:get",
        "statistics:getToday",
        "logs:getStats",
        "requestLogs:getStats",
        "session:getConfig",
        "managementApi:getConfig",
        "contextManagement:getConfig",
        "accounts:getAll",
    };

    public Chat2ApiIpcBridge(
        LocalOpenAiServer localApi,
        WebInjectService web,
        Func<Window?> owner,
        Func<AppConfig, CancellationToken, Task>? onStackSync = null)
    {
        _localApi = localApi;
        _web = web;
        _owner = owner;
        _onStackSync = onStackSync;
        _config = ConfigStore.Load();
        if (!_localApi.IsListening)
            _localApi.Start();
    }

    public void RefreshConfig(AppConfig config)
    {
        _config = config;
        _localApi.UpdateConfig(config);
        _readCache.Clear();
    }

    public async Task<object?> InvokeAsync(string channel, JsonElement[] args, CancellationToken ct = default)
    {
        if (CachedReadChannels.Contains(channel) && args.Length == 0 && TryGetCached(channel, out var hit))
            return hit;

        var result = await InvokeCoreAsync(channel, args, ct);

        if (CachedReadChannels.Contains(channel) && args.Length == 0 && result is not null)
            SetCached(channel, result);

        return result;
    }

    private bool TryGetCached(string channel, out object? value)
    {
        value = null;
        if (!_readCache.TryGetValue(channel, out var entry)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > entry.ExpiresAt)
        {
            _readCache.Remove(channel);
            return false;
        }
        value = entry.Value;
        return true;
    }

    private void SetCached(string channel, object value) =>
        _readCache[channel] = (value, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2000);

    private async Task<object?> InvokeCoreAsync(string channel, JsonElement[] args, CancellationToken ct)
    {
        return channel switch
        {
            "proxy:start" => await ProxyStartAsync(args),
            "proxy:stop" => ProxyStop(),
            "proxy:getStatus" => ProxyStatus(),
            "proxy:getStatistics" => EmptyProxyStatistics(),
            "config:get" => BuildChat2ApiConfig(),
            "config:update" => await ConfigUpdate(args),
            "store:get" => StoreGet(args),
            "store:set" => await StoreSet(args),
            "store:delete" => StoreDelete(args),
            "store:clearAll" => StoreClearAll(),
            "store:retryInit" => new { success = true },
            "providers:getAll" => GetProviders(),
            "providers:getBuiltin" => GetBuiltinProviders(),
            "providers:checkStatus" => CheckProviderStatus(args),
            "providers:checkAllStatus" => CheckAllProviderStatus(),
            "providers:getEffectiveModels" => GetEffectiveModels(args),
            "providers:updateModels" => new { success = true, modelsCount = Chat2ApiCompat.ListModels(_config).Count },
            "providers:addCustomModel" => await AddCustomModelAsync(args),
            "providers:removeModel" => await RemoveCustomModelAsync(args),
            "providers:resetModels" => await ResetModelsAsync(args),
            "providers:add" => Unsupported("内嵌模式仅支持 DeepSeek 网页供应商"),
            "providers:update" => Unsupported("内嵌模式不支持修改内置供应商"),
            "providers:delete" => false,
            "providers:duplicate" => Unsupported("内嵌模式不支持"),
            "providers:export" => "{}",
            "providers:import" => Unsupported("内嵌模式不支持"),
            "accounts:add" => Unsupported("请在主窗口登录 DeepSeek"),
            "accounts:update" => Unsupported("Token 由主窗口同步"),
            "accounts:delete" => false,
            "accounts:getAll" => GetAccounts(args),
            "accounts:getById" => GetAccountById(args),
            "accounts:getByProvider" => GetAccountsByProvider(args),
            "accounts:validate" => await ValidateAccountAsync(args, ct),
            "accounts:validateToken" => await ValidateTokenAsync(args, ct),
            "accounts:getCredits" => null,
            "accounts:clearChats" => new { success = true },
            "session:getConfig" => SessionConfig(),
            "session:updateConfig" => await SessionUpdateConfig(args),
            "session:getAll" => ListSessions(),
            "session:getActive" => ListSessions(activeOnly: true),
            "session:getById" => SessionById(args),
            "session:delete" => SessionDelete(args),
            "session:clearAll" => SessionClearAll(),
            "session:cleanExpired" => 0,
            "logs:get" => Array.Empty<object>(),
            "logs:getStats" => EmptyLogStats(),
            "logs:getTrend" => Array.Empty<object>(),
            "logs:getAccountTrend" => Array.Empty<object>(),
            "logs:clear" => null,
            "logs:export" => "[]",
            "logs:getById" => null,
            "requestLogs:get" => Array.Empty<object>(),
            "requestLogs:getStats" => EmptyRequestLogStats(),
            "requestLogs:getTrend" => Array.Empty<object>(),
            "requestLogs:clear" => null,
            "requestLogs:getById" => null,
            "statistics:get" => EmptyPersistentStatistics(),
            "statistics:getToday" => EmptyDailyStatistics(),
            "prompts:getAll" => Array.Empty<object>(),
            "prompts:getBuiltin" => Array.Empty<object>(),
            "prompts:getCustom" => Array.Empty<object>(),
            "prompts:getByType" => Array.Empty<object>(),
            "managementApi:getConfig" => ManagementApiConfig(),
            "managementApi:updateConfig" => await ManagementApiUpdateAsync(args, ct),
            "managementApi:generateSecret" => Guid.NewGuid().ToString("N"),
            "contextManagement:getConfig" => ContextManagementConfig(),
            "contextManagement:updateConfig" => await ContextManagementUpdateAsync(args, ct),
            "toolCalling:getStatus" => ToolCallingGetStatus(),
            "toolCalling:runSmoke" => await ToolCallingRunSmokeAsync(args, ct),
            "app:getVersion" => "1.3.0-edge",
            "app:checkUpdate" => new
            {
                hasUpdate = false,
                currentVersion = "embedded",
                latestVersion = "embedded"
            },
            "app:getUpdateStatus" => new
            {
                checking = false,
                available = false,
                downloading = false,
                downloaded = false,
                error = (string?)null,
                progress = (object?)null,
                version = (string?)null,
                releaseDate = (string?)null,
                releaseNotes = (string?)null
            },
            "app:downloadUpdate" => null,
            "app:installUpdate" => null,
            "app:minimize" => MinimizeWindow(),
            "app:maximize" => MaximizeWindow(),
            "app:close" => CloseWindow(),
            "app:showWindow" => ShowWindow(),
            "app:hideWindow" => HideWindow(),
            "app:openExternal" => OpenExternal(args),
            "oauth:startInAppLogin" => await OAuthInAppLoginAsync(args, ct),
            "oauth:cancelInAppLogin" => null,
            "oauth:inAppLoginStatus" => false,
            "oauth:getStatus" => "idle",
            "oauth:loginWithToken" => await OAuthLoginWithTokenAsync(args, ct),
            "oauth:validateToken" => await ValidateTokenAsync(args, ct),
            _ => throw new NotSupportedException($"IPC channel not supported in embedded mode: {channel}")
        };
    }

    private static object Unsupported(string message) => throw new InvalidOperationException(message);

    private Task<bool> ProxyStartAsync(JsonElement[] args)
    {
        if (!_localApi.IsListening)
            _localApi.Start();
        return Task.FromResult(true);
    }

    private bool ProxyStop()
    {
        // 内嵌模式：服务由桌面端托管，不允许从管理台停止
        return true;
    }

    private object ProxyStatus()
    {
        var externalPort = InternalChatChannel.ResolveExternalApiPort(_config);
        return new
        {
            isRunning = true,
            embedded = true,
            mode = "internal",
            internalChannel = InternalChatChannel.DesktopV1,
            port = _config.EnableExternalOpenAiApi ? externalPort : 0,
            host = _config.EnableExternalOpenAiApi ? "127.0.0.1" : "",
            externalApiEnabled = _config.EnableExternalOpenAiApi,
            externalApiBaseUrl = _config.EnableExternalOpenAiApi
                ? InternalChatChannel.GetExternalApiBaseUrl(_config)
                : null,
            uptime = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _proxyStartedAt),
            connections = 0
        };
    }

    private static object EmptyProxyStatistics() => new
    {
        totalRequests = 0,
        successRequests = 0,
        failedRequests = 0,
        avgLatency = 0,
        requestsPerMinute = 0,
        activeConnections = 0,
        modelUsage = new Dictionary<string, int>(),
        providerUsage = new Dictionary<string, int>(),
        accountUsage = new Dictionary<string, int>()
    };

    private object BuildChat2ApiConfig()
    {
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        var mappings = _config.ModelMappings.ToDictionary(
            m => m.RequestModel,
            m => new
            {
                requestModel = m.RequestModel,
                actualModel = m.ActualModel,
                preferredProviderId = "deepseek",
                preferredAccountId = EmbeddedAccountId
            },
            StringComparer.OrdinalIgnoreCase);

        return new
        {
            proxyPort = _config.EnableExternalOpenAiApi
                ? InternalChatChannel.ResolveExternalApiPort(_config)
                : 0,
            proxyHost = "127.0.0.1",
            embeddedMode = true,
            internalChannel = InternalChatChannel.DesktopV1,
            loadBalanceStrategy = "round-robin",
            modelMappings = mappings,
            theme = "light",
            autoStart = true,
            autoStartProxy = false,
            minimizeToTray = false,
            logLevel = "info",
            logRetentionDays = 7,
            requestLogConfig = new { enabled = true, maxEntries = 500, retentionDays = 7 },
            requestTimeout = Math.Max(60_000, _config.Chat2ApiSessionTimeoutMinutes * 60_000),
            retryCount = 2,
            apiKeys = MapApiKeys(),
            enableApiKey = _config.EnableLocalApiKeyAuth,
            oauthProxyMode = "none",
            sessionConfig = SessionConfig(),
            toolCallingConfig = GetToolCallingConfig(),
            managementApi = ManagementApiConfig(),
            contextManagement = ContextManagementConfig(),
            language = "zh-CN",
            deepseekDesktop = BuildDesktopStackInfo()
        };
    }

    private object BuildDesktopStackInfo()
    {
        var snap = Chat2ApiProviderService.Build(_config);
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);

        return new
        {
            app = DeepSeekDesktopApp.DisplayName,
            loggedIn,
            loginHint = loggedIn ? null : "请在 DeepSeek Desktop 主窗口打开 chat.deepseek.com 并登录",
            internalChannel = InternalChatChannel.DesktopV1,
            externalApiEnabled = _config.EnableExternalOpenAiApi,
            externalApiBaseUrl = _config.EnableExternalOpenAiApi
                ? InternalChatChannel.GetExternalApiBaseUrl(_config)
                : null,
            harnessEngine = "native",
            tuiRuntimeUrl = snap.TuiRuntimeUrl,
            tuiConfigPath = AgentDesktopConfigSync.ConfigPath,
            integrationFile = Chat2ApiProviderService.IntegrationFilePath,
            defaultWorkMode = _config.DefaultWorkMode,
            agentStrategy = _config.DefaultAgentStrategy,
            agentDeepThinking = _config.AgentDeepThinking,
            agentWebSearch = _config.AgentWebSearch,
            sessionMode = _config.Chat2ApiSessionMode,
            modelMappingCount = _config.ModelMappings.Count,
            providerOnline = snap.Online,
            chat2ApiSummary = snap.Description
        };
    }

    private async Task<object> ConfigUpdate(JsonElement[] args)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
            await ApplyConfigPatchAsync(args[0]);
        return true;
    }

    private async Task ApplyConfigPatchAsync(JsonElement patch)
    {
        if (patch.TryGetProperty("toolCallingConfig", out var toolCalling) &&
            toolCalling.ValueKind == JsonValueKind.Object)
            _uiStore["toolCallingConfig"] = toolCalling.Clone();

        Chat2ApiEmbeddedConfigApplicator.ApplyPatch(_config, patch);
        await PersistAndSyncAsync();
    }

    private JsonElement GetToolCallingConfigElement()
    {
        if (_uiStore.TryGetValue("toolCallingConfig", out var stored) &&
            stored.ValueKind == JsonValueKind.Object)
            return stored;

        return JsonSerializer.SerializeToElement(CreateDefaultToolCallingConfig(), JsonOptions);
    }

    private object GetToolCallingConfig() => GetToolCallingConfigElement();

    private static object CreateDefaultToolCallingConfig() => new
    {
        enabled = true,
        mode = "auto",
        clientAdapterId = "standard-openai-tools",
        diagnosticsEnabled = false,
        advanced = new { promptPreviewEnabled = false, customPromptTemplate = (string?)null }
    };

    private object ToolCallingGetStatus()
    {
        var cfg = GetToolCallingConfigElement();
        var enabled = ToolCallingIsEnabled(cfg);
        return new
        {
            enabled,
            config = cfg,
            clientAdapters = new[]
            {
                new { id = "standard-openai-tools", label = "Standard OpenAI Tools", status = "ready" },
                new { id = "cherry-studio-mcp", label = "Cherry Studio MCP", status = "ready" }
            },
            providerSupport = new[]
            {
                new { providerId = "deepseek", label = "DEEPSEEK", managed = true, protocolId = "managed_xml", status = "supported" },
                new { providerId = "kimi", label = "KIMI", managed = true, protocolId = "managed_xml", status = "supported" },
                new { providerId = "glm", label = "GLM", managed = true, protocolId = "managed_xml", status = "supported" },
                new { providerId = "qwen", label = "QWEN", managed = true, protocolId = "managed_xml", status = "supported" },
                new { providerId = "mimo", label = "MIMO", managed = true, protocolId = "managed_xml", status = "supported" }
            }
        };
    }

    private async Task<object> ToolCallingRunSmokeAsync(JsonElement[] args, CancellationToken ct)
    {
        if (!ToolCallingIsEnabled(GetToolCallingConfigElement()))
            return new { success = false, error = new { message = "托管工具调用未启用，请将模式设为 auto 或 force。" } };

        if (string.IsNullOrWhiteSpace(_config.WebUserToken))
            return new { success = false, error = new { message = "请先在 DeepSeek 主窗口「普通对话」登录 DeepSeek。" } };

        try
        {
            var messages = new List<ChatMessage>
            {
                new() { Role = "user", Content = "请只回复一个词：OK" }
            };
            var result = await _web.WebChatAsync(
                messages,
                _config.Model,
                thinking: false,
                search: false,
                ct,
                _config.WebUserToken).ConfigureAwait(false);

            var text = result.Content?.Trim();
            var ok = !string.IsNullOrEmpty(text) && !string.Equals(text, "(无回复)", StringComparison.Ordinal);
            return ok
                ? new { success = true }
                : new { success = false, error = new { message = "网页会话未返回正文，请确认已登录并重试。" } };
        }
        catch (Exception ex)
        {
            return new { success = false, error = new { message = ex.Message } };
        }
    }

    private static bool ToolCallingIsEnabled(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return true;

        var enabled = !el.TryGetProperty("enabled", out var en) || en.GetBoolean();
        var mode = el.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : "auto";
        return enabled && !string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PersistAndSyncAsync(CancellationToken ct = default)
    {
        ConfigStore.Save(_config);
        _localApi.UpdateConfig(_config);
        _readCache.Clear();
        if (_onStackSync is not null)
            await _onStackSync(_config, ct).ConfigureAwait(false);
    }

    private void ApplyConfigPatch(JsonElement patch) =>
        Chat2ApiEmbeddedConfigApplicator.ApplyPatch(_config, patch);

    private object? StoreGet(JsonElement[] args)
    {
        var key = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrEmpty(key)) return null;
        if (key == "config") return BuildChat2ApiConfig();
        return _uiStore.TryGetValue(key, out var v) ? v : null;
    }

    private async Task<object?> StoreSet(JsonElement[] args)
    {
        if (args.Length < 2) return null;
        var key = args[0].GetString();
        if (string.IsNullOrEmpty(key)) return null;
        if (key == "config" && args[1].ValueKind == JsonValueKind.Object)
            await ApplyConfigPatchAsync(args[1]);
        else
        {
            _uiStore[key] = args[1].Clone();
            await PersistAndSyncAsync();
        }

        return null;
    }

    private object? StoreDelete(JsonElement[] args)
    {
        var key = args.Length > 0 ? args[0].GetString() : null;
        if (!string.IsNullOrEmpty(key))
            _uiStore.Remove(key);
        return null;
    }

    private object? StoreClearAll()
    {
        _uiStore.Clear();
        return null;
    }

    private const string EmbeddedAccountId = "deepseek-web-session";

    private object[] GetProviders()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);
        return
        [
            new
            {
                id = "deepseek",
                name = "DeepSeek",
                type = "builtin",
                authType = "userToken",
                apiEndpoint = _config.WebApiBaseUrl,
                chatPath = "/v0/chat/completion",
                headers = new Dictionary<string, string>(),
                enabled = true,
                createdAt = now,
                updatedAt = now,
                description = "DeepSeek 网页会话（内嵌于 DeepSeek Desktop）",
                supportedModels = Chat2ApiCompat.ListModelIds(_config).ToArray(),
                status = loggedIn ? "online" : "offline",
                lastStatusCheck = now
            }
        ];
    }

    private object[] GetBuiltinProviders() =>
    [
        new
        {
            id = "deepseek",
            name = "DeepSeek",
            type = "builtin",
            authType = "userToken",
            apiEndpoint = "https://chat.deepseek.com/api",
            chatPath = "/v0/chat/completion",
            headers = new Dictionary<string, string>(),
            enabled = true,
            description = "DeepSeek 智能对话助手，经 DeepSeek Desktop 网页会话与 DeepSeek-TUI 统一调度",
            supportedModels = Chat2ApiCompat.ListModelIds(_config).ToArray(),
            credentialFields = new[]
            {
                new
                {
                    name = "token",
                    label = "网页会话 Token",
                    type = "password",
                    required = true,
                    placeholder = "由主窗口登录后自动同步",
                    helpText = "在 DeepSeek Desktop 主窗口登录 chat.deepseek.com 后自动写入"
                }
            },
            tokenCheckEndpoint = "/v0/users/current",
            tokenCheckMethod = "GET"
        }
    ];

    private object CheckProviderStatus(JsonElement[] args)
    {
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);
        return new
        {
            providerId = "deepseek",
            status = loggedIn ? "online" : "offline",
            latency = loggedIn ? 32 : (int?)null,
            error = loggedIn ? null : "未登录 DeepSeek 网页"
        };
    }

    private object CheckAllProviderStatus() =>
        new Dictionary<string, object> { ["deepseek"] = CheckProviderStatus([]) };

    private object GetEffectiveModels(JsonElement[] args)
    {
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        return Chat2ApiCompat.ListModelIds(_config).Select(id => new
        {
            displayName = id,
            actualModelId = id,
            source = "builtin",
            isCustom = false
        }).ToArray();
    }

    private async Task<object> AddCustomModelAsync(JsonElement[] args)
    {
        if (args.Length >= 2 &&
            args[1].TryGetProperty("displayName", out var dn) &&
            args[1].TryGetProperty("actualModelId", out var am))
        {
            _config.ModelMappings.Add(new ModelMappingEntry
            {
                RequestModel = dn.GetString() ?? "",
                ActualModel = am.GetString() ?? ""
            });
            await PersistAndSyncAsync();
        }

        return new { success = true, models = GetEffectiveModels(args), error = (string?)null };
    }

    private async Task<object> RemoveCustomModelAsync(JsonElement[] args)
    {
        if (args.Length >= 2)
        {
            var name = args[1].GetString();
            _config.ModelMappings.RemoveAll(m =>
                string.Equals(m.RequestModel, name, StringComparison.OrdinalIgnoreCase));
            await PersistAndSyncAsync();
        }

        return new { success = true, models = GetEffectiveModels(args), error = (string?)null };
    }

    private async Task<object> ResetModelsAsync(JsonElement[] args)
    {
        _config.ModelMappings.Clear();
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        await PersistAndSyncAsync();
        return new { success = true, models = GetEffectiveModels(args), error = (string?)null };
    }

    private object[] GetAccounts(JsonElement[] args)
    {
        var includeCreds = args.Length > 0 && args[0].ValueKind == JsonValueKind.True;
        return [BuildEmbeddedAccount(includeCreds)];
    }

    private object? GetAccountById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (!string.Equals(id, EmbeddedAccountId, StringComparison.Ordinal))
            return null;
        var include = args.Length > 1 && args[1].ValueKind == JsonValueKind.True;
        return BuildEmbeddedAccount(include);
    }

    private object[] GetAccountsByProvider(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : null;
        if (!string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
            return [];
        return [BuildEmbeddedAccount(false)];
    }

    private object BuildEmbeddedAccount(bool includeCredentials)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var loggedIn = !string.IsNullOrWhiteSpace(_config.WebUserToken);
        var creds = includeCredentials
            ? new Dictionary<string, string> { ["token"] = _config.WebUserToken }
            : new Dictionary<string, string> { ["token"] = loggedIn ? "••••••••" : "" };

        return new
        {
            id = EmbeddedAccountId,
            providerId = "deepseek",
            name = "DeepSeek 网页会话",
            email = "",
            credentials = creds,
            status = loggedIn ? "active" : "inactive",
            lastUsed = now,
            createdAt = now,
            updatedAt = now,
            requestCount = 0
        };
    }

    private async Task<object> ValidateAccountAsync(JsonElement[] args, CancellationToken ct)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (!string.Equals(id, EmbeddedAccountId, StringComparison.Ordinal))
            return false;
        var health = await _web.ProbeChat2ApiHealthAsync(_config.WebUserToken, _localApi.BaseUrl, ct);
        return health.CanChat;
    }

    private async Task<object> ValidateTokenAsync(JsonElement[] args, CancellationToken ct)
    {
        var health = await _web.ProbeChat2ApiHealthAsync(_config.WebUserToken, _localApi.BaseUrl, ct);
        return new
        {
            valid = health.CanChat,
            error = health.CanChat ? null : health.Error ?? "Token 无效或未登录",
            userInfo = health.CanChat
                ? new { name = "DeepSeek", email = "" }
                : null
        };
    }

    private object SessionConfig() => new
    {
        mode = string.Equals(_config.Chat2ApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase)
            ? "multi"
            : "single",
        sessionTimeout = _config.Chat2ApiSessionTimeoutMinutes,
        maxMessagesPerSession = _config.Chat2ApiMaxMessagesPerSession,
        deleteAfterTimeout = true,
        maxSessionsPerAccount = 20
    };

    private async Task<object?> SessionUpdateConfig(JsonElement[] args)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
            await ApplyConfigPatchAsync(args[0]);
        return null;
    }

    private object[] ListSessions(bool activeOnly = false)
    {
        var list = _localApi.ListSessions();
        return list.Select(s => new
        {
            id = s.ClientSessionId,
            providerId = "deepseek",
            accountId = EmbeddedAccountId,
            providerSessionId = s.WebSessionId,
            sessionType = "chat",
            messages = Array.Empty<object>(),
            createdAt = s.LastUsedAt,
            lastActiveAt = s.LastUsedAt,
            status = "active",
            model = (string?)null
        }).ToArray();
    }

    private object? SessionById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrEmpty(id)) return null;
        var hit = _localApi.ListSessions().FirstOrDefault(s => s.ClientSessionId == id);
        if (hit is null) return null;
        return new
        {
            id = hit.ClientSessionId,
            providerId = "deepseek",
            accountId = EmbeddedAccountId,
            providerSessionId = hit.WebSessionId,
            sessionType = "chat",
            messages = Array.Empty<object>(),
            createdAt = hit.LastUsedAt,
            lastActiveAt = hit.LastUsedAt,
            status = "active",
            model = (string?)null
        };
    }

    private object SessionDelete(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        return !string.IsNullOrEmpty(id) && _localApi.DeleteSession(id);
    }

    private object? SessionClearAll()
    {
        foreach (var s in _localApi.ListSessions())
            _localApi.DeleteSession(s.ClientSessionId);
        return null;
    }

    private object[] MapApiKeys() =>
        _config.LocalApiKeys.Select(k => new
        {
            id = k.Id,
            name = k.Name,
            key = k.Key,
            enabled = k.Enabled,
            createdAt = k.CreatedAt,
            lastUsedAt = k.LastUsedAt,
            usageCount = k.UsageCount,
            description = k.Description
        }).ToArray();

    private static object EmptyLogStats() => new { total = 0, info = 0, warn = 0, error = 0, debug = 0 };

    private static object EmptyRequestLogStats() => new
    {
        total = 0,
        success = 0,
        error = 0,
        todayTotal = 0,
        todaySuccess = 0,
        todayError = 0
    };

    private static object EmptyPersistentStatistics() => new
    {
        totalRequests = 0,
        successRequests = 0,
        failedRequests = 0,
        totalLatency = 0L,
        lastUpdated = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        modelUsage = new Dictionary<string, int>(),
        providerUsage = new Dictionary<string, int>(),
        accountUsage = new Dictionary<string, int>(),
        dailyStats = new Dictionary<string, object>()
    };

    private static object EmptyDailyStatistics()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return new
        {
            date = today,
            totalRequests = 0,
            successRequests = 0,
            failedRequests = 0,
            totalLatency = 0L,
            modelUsage = new Dictionary<string, int>(),
            providerUsage = new Dictionary<string, int>()
        };
    }

    private object ManagementApiConfig() => new
    {
        enableManagementApi = _config.EnableExternalOpenAiApi,
        managementApiSecret = _config.EnableLocalApiKeyAuth ? DeepSeekDesktopApp.LocalApiKeyFallback : "",
        managementApiPort = InternalChatChannel.ResolveExternalApiPort(_config)
    };

    private async Task<object> ManagementApiUpdateAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
            await ApplyConfigPatchAsync(args[0]);
        return ManagementApiConfig();
    }

    private object ContextManagementConfig() => new
    {
        enabled = true,
        strategies = new
        {
            slidingWindow = new { enabled = true, maxMessages = _config.Chat2ApiMaxMessagesPerSession },
            tokenLimit = new { enabled = false, maxTokens = 8000 },
            summary = new { enabled = false, keepRecentMessages = 6 }
        },
        executionOrder = new[] { "slidingWindow", "tokenLimit", "summary" }
    };

    private async Task<object> ContextManagementUpdateAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
            await ApplyConfigPatchAsync(args[0]);
        return ContextManagementConfig();
    }

    private object? MinimizeWindow()
    {
        _owner()?.Dispatcher.Invoke(() => _owner()?.WindowState = WindowState.Minimized);
        return null;
    }

    private object? MaximizeWindow()
    {
        _owner()?.Dispatcher.Invoke(() =>
        {
            var w = _owner();
            if (w is null) return;
            w.WindowState = w.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        });
        return null;
    }

    private object? CloseWindow()
    {
        _owner()?.Dispatcher.Invoke(() => _owner()?.Close());
        return null;
    }

    private object? ShowWindow()
    {
        _owner()?.Dispatcher.Invoke(() =>
        {
            var w = _owner();
            if (w is null) return;
            w.Show();
            w.Activate();
        });
        return null;
    }

    private object? HideWindow()
    {
        _owner()?.Dispatcher.Invoke(() => _owner()?.Hide());
        return null;
    }

    private object? OpenExternal(JsonElement[] args)
    {
        var url = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(url)) return null;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return null;
    }

    private Task<object> OAuthInAppLoginAsync(JsonElement[] args, CancellationToken ct) =>
        Task.FromResult<object>(new
        {
            success = !string.IsNullOrWhiteSpace(_config.WebUserToken),
            providerId = "deepseek",
            providerType = "deepseek",
            error = string.IsNullOrWhiteSpace(_config.WebUserToken)
                ? "请在 DeepSeek Desktop 主窗口登录 chat.deepseek.com"
                : null
        });

    private async Task<object> OAuthLoginWithTokenAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].ValueKind != JsonValueKind.Object)
            return new { success = false, error = "invalid payload" };

        var payload = args[0];
        if (payload.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String)
        {
            var token = tok.GetString();
            if (!string.IsNullOrWhiteSpace(token))
            {
                _config.WebUserToken = token!;
                await _web.SyncApiBridgeTokenAsync(token);
                await PersistAndSyncAsync(ct);
            }
        }

        var health = await _web.ProbeChat2ApiHealthAsync(_config.WebUserToken, _localApi.BaseUrl, ct);
        return new
        {
            success = health.CanChat,
            providerId = "deepseek",
            providerType = "deepseek",
            account = BuildEmbeddedAccount(true),
            error = health.CanChat ? null : health.Error
        };
    }
}
