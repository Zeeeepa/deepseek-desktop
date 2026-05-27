using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.OAuth;

namespace DeepSeekBrowser.Services;

public sealed partial class DsdApiIpcBridge
{
    private async Task<object?> LegacyChannelDispatchAsync(string channel, JsonElement[] args, CancellationToken ct)
    {
        return channel switch
        {
            "proxy:start" => await ProxyStartAsync(args),
            "proxy:stop" => ProxyStop(),
            "proxy:getStatus" => ProxyStatus(),
            "proxy:getStatistics" => ProxyStatistics(),
            "proxy:resetStatistics" => ProxyResetStatistics(),
            "store:get" => StoreGet(args),
            "store:set" => await StoreSet(args),
            "store:delete" => StoreDelete(args),
            "store:clearAll" => StoreClearAll(),
            "store:retryInit" => new { success = true },
            "providers:checkStatus" => CheckProviderStatus(args),
            "providers:getEffectiveModels" => GetEffectiveModels(args),
            "providers:syncModels" => await SyncProviderModelsAsync(args, ct),
            "providers:updateModels" => await UpdateProviderModelsAsync(args, ct),
            "providers:addCustomModel" => await AddCustomModelAsync(args),
            "providers:removeModel" => await RemoveCustomModelAsync(args),
            "providers:resetModels" => await ResetModelsAsync(args),
            "providers:add" => await AddProviderAsync(args),
            "providers:update" => await UpdateProviderAsync(args),
            "providers:delete" => DeleteProvider(args),
            "providers:duplicate" => await DuplicateProviderAsync(args),
            "providers:export" => ExportProvider(args),
            "providers:import" => await ImportProviderAsync(args),
            "accounts:add" => await AddAccountAsync(args),
            "accounts:update" => await UpdateAccountAsync(args),
            "accounts:delete" => await DeleteAccountAsync(args),
            "accounts:getById" => GetAccountById(args),
            "accounts:getByProvider" => GetAccountsByProvider(args),
            "accounts:validate" => await ValidateAccountAsync(args, ct),
            "accounts:validateToken" => await ValidateTokenAsync(args, ct),
            "accounts:getCredits" => await GetAccountCreditsAsync(args, ct),
            "accounts:clearChats" => await ClearAccountChatsAsync(args, ct),
            "session:getAll" => ListSessions(),
            "session:getActive" => ListSessions(activeOnly: true),
            "session:getById" => SessionById(args),
            "session:getByAccount" => SessionsByAccount(args),
            "session:getByProvider" => SessionsByProvider(args),
            "session:delete" => SessionDelete(args),
            "session:clearAll" => SessionClearAll(),
            "session:cleanExpired" => CleanExpiredSessions(),
            "logs:get" => AppLogsGet(args),
            "logs:getStats" => DsdAppLogStore.Instance.GetStats(),
            "logs:getTrend" => DsdAppLogStore.Instance.GetTrend(ParseTrendDays(args)),
            "logs:getAccountTrend" => GetAccountTrend(args),
            "logs:clear" => AppLogsClear(),
            "logs:export" => DsdAppLogStore.Instance.Export(ParseExportFormat(args)),
            "logs:getById" => AppLogsGetById(args),
            "requestLogs:get" => RequestLogsGet(args),
            "requestLogs:getStats" => DsdApiRequestLogStore.Instance.GetStats(),
            "requestLogs:getTrend" => RequestLogsGetTrend(args),
            "requestLogs:clear" => RequestLogsClear(),
            "requestLogs:getById" => RequestLogsGetById(args),
            "statistics:get" => DsdApiRequestLogStore.Instance.BuildPersistentStatistics(),
            "statistics:getToday" => StatisticsGetToday(),
            "prompts:getAll" => PromptsGetAll(),
            "prompts:getBuiltin" => PromptsGetBuiltin(),
            "prompts:getCustom" => PromptsGetCustom(),
            "prompts:getById" => PromptsGetById(args),
            "prompts:add" => PromptsAdd(args),
            "prompts:update" => PromptsUpdate(args),
            "prompts:delete" => PromptsDelete(args),
            "prompts:getByType" => PromptsGetByType(args),
            "managementApi:getConfig" => ManagementApiConfig(),
            "managementApi:updateConfig" => await ManagementApiUpdateAsync(args, ct),
            "managementApi:generateSecret" => Guid.NewGuid().ToString("N"),
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
            "oauth:startLogin" => await OAuthBrowserLoginAsync(args, ct),
            "oauth:cancelLogin" => CancelOAuthLogin(),
            "oauth:callback" => OAuthCallback(args),
            "oauth:startInAppLogin" => await OAuthInAppLoginAsync(args, ct),
            "oauth:cancelInAppLogin" => CancelOAuthInAppLogin(),
            "oauth:inAppLoginStatus" => _oauthLogin.IsOpen,
            "oauth:getStatus" => OAuthGetStatus(),
            "oauth:loginWithToken" => await OAuthLoginWithTokenAsync(args, ct),
            "oauth:validateToken" => await ValidateTokenAsync(args, ct),
            "oauth:refreshToken" => await OAuthRefreshTokenAsync(args, ct),
            _ => throw new NotSupportedException($"IPC channel not supported in embedded mode: {channel}")
        };
    }

    private static object Unsupported(string message) => throw new InvalidOperationException(message);

    private Task<bool> ProxyStartAsync(JsonElement[] args)
    {
        if (!_localApi.IsListening)
            _localApi.Start();
        EmitProxyStatusChanged();
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

    private object ProxyStatistics() => DsdApiRequestLogStore.Instance.BuildPersistentStatistics();

    private object? ProxyResetStatistics()
    {
        _readCache.Remove("proxy:getStatistics");
        _readCache.Remove("statistics:get");
        _readCache.Remove("statistics:getToday");
        return null;
    }

    private object BuildDsdApiConfig()
    {
        DsdOpenAiCompat.EnsureDefaultMappings(_config);
        var mappings = _config.ModelMappings.ToDictionary(
            m => m.RequestModel,
            m => new
            {
                requestModel = m.RequestModel,
                actualModel = m.ActualModel,
                preferredProviderId = m.PreferredProviderId,
                preferredAccountId = m.PreferredAccountId
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
            loadBalanceStrategy = string.IsNullOrWhiteSpace(_config.DsdApiLoadBalanceStrategy)
                ? "round-robin"
                : _config.DsdApiLoadBalanceStrategy,
            accountWeights = _config.DsdApiAccountWeights.Select(w => new
            {
                accountId = w.AccountId,
                weight = w.Weight
            }).ToArray(),
            modelMappings = mappings,
            theme = "light",
            autoStart = true,
            autoStartProxy = false,
            logLevel = "info",
            logRetentionDays = 7,
            requestLogConfig = new { enabled = true, maxEntries = 500, retentionDays = 7 },
            requestTimeout = Math.Max(60_000, _config.DsdApiSessionTimeoutMinutes * 60_000),
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
        var snap = DsdApiProviderService.Build(_config);
        var apiAccounts = ProviderAccountStore.ByProvider("deepseek")
            .Count(a => a.Status == "active"
                        && !string.IsNullOrWhiteSpace(
                            AccountCredentials.ResolveWebUserToken(a, _config)));
        var loggedIn = apiAccounts > 0;

        return new
        {
            app = DeepSeekDesktopApp.DisplayName,
            loggedIn,
            loginHint = loggedIn
                ? null
                : "请在 API 管理中手动添加 DeepSeek 账户（普通对话登录不会自动同步）",
            internalChannel = InternalChatChannel.DesktopV1,
            externalApiEnabled = _config.EnableExternalOpenAiApi,
            externalApiBaseUrl = _config.EnableExternalOpenAiApi
                ? InternalChatChannel.GetExternalApiBaseUrl(_config)
                : null,
            harnessEngine = "native",
            tuiRuntimeUrl = snap.TuiRuntimeUrl,
            tuiConfigPath = AgentDesktopConfigSync.ConfigPath,
            integrationFile = DsdApiProviderService.IntegrationFilePath,
            defaultWorkMode = _config.DefaultWorkMode,
            agentStrategy = _config.DefaultAgentStrategy,
            agentDeepThinking = _config.AgentDeepThinking,
            agentWebSearch = _config.AgentWebSearch,
            sessionMode = _config.DsdApiSessionMode,
            modelMappingCount = _config.ModelMappings.Count,
            providerOnline = snap.Online,
            chat2ApiSummary = snap.Description
        };
    }

    private async Task<object?> ConfigUpdate(JsonElement[] args)
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

        if (patch.TryGetProperty("requestLogConfig", out var requestLogConfig) &&
            requestLogConfig.ValueKind == JsonValueKind.Object)
        {
            _uiStore["requestLogConfig"] = requestLogConfig.Clone();
            DsdApiRequestLogStore.Instance.Configure(
                DsdApiRequestLogStore.RequestLogConfig.FromJson(requestLogConfig));
        }

        DsdEmbeddedConfigApplicator.ApplyPatch(_config, patch);
        await PersistAndSyncAsync();
    }

    private object RequestLogsGet(JsonElement[] args)
    {
        DsdApiRequestLogStore.RequestLogQuery? query = null;
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
        {
            var o = args[0];
            int? limit = null;
            if (o.TryGetProperty("limit", out var limitEl) && limitEl.TryGetInt32(out var n))
                limit = n;
            query = new DsdApiRequestLogStore.RequestLogQuery
            {
                Status = o.TryGetProperty("status", out var st) ? st.GetString() : null,
                ProviderId = o.TryGetProperty("providerId", out var pid) ? pid.GetString() : null,
                Limit = limit
            };
        }

        return DsdApiRequestLogStore.Instance.GetLogs(query);
    }

    private object RequestLogsGetTrend(JsonElement[] args)
    {
        var days = 7;
        if (args.Length > 0 && args[0].TryGetInt32(out var d))
            days = d;
        return DsdApiRequestLogStore.Instance.GetTrend(days);
    }

    private object? RequestLogsClear()
    {
        DsdApiRequestLogStore.Instance.Clear();
        _readCache.Clear();
        return null;
    }

    private object? RequestLogsGetById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        return string.IsNullOrEmpty(id) ? null : DsdApiRequestLogStore.Instance.GetById(id);
    }

    private object StatisticsGetToday()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var trend = DsdApiRequestLogStore.Instance.GetTrend(1).FirstOrDefault();
        if (trend is null || trend.Date != today)
        {
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

        return new
        {
            date = today,
            totalRequests = trend.Total,
            successRequests = trend.Success,
            failedRequests = trend.Error,
            totalLatency = trend.Success > 0 ? (long)trend.AvgLatency * trend.Success : 0L,
            modelUsage = new Dictionary<string, int>(),
            providerUsage = new Dictionary<string, int>()
        };
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
            }
        };
    }

    private async Task<object> ToolCallingRunSmokeAsync(JsonElement[] args, CancellationToken ct)
    {
        if (!ToolCallingIsEnabled(GetToolCallingConfigElement()))
            return new { success = false, error = new { message = "托管工具调用未启用，请将模式设为 auto 或 force。" } };

        var token = ResolveDeepSeekSmokeToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new
            {
                success = false,
                error = new
                {
                    message = "请先在 API 管理 → DeepSeek 添加至少一个带有效 Token 的账户（保留带邮箱的那条即可）。"
                }
            };
        }

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
                token).ConfigureAwait(false);

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

    private string? ResolveDeepSeekSmokeToken()
    {
        foreach (var account in ProviderAccountStore.ByProvider("deepseek"))
        {
            if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
                continue;
            var token = AccountCredentials.ResolveWebUserToken(account, _config, allowConfigFallback: false);
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }

        return AccountCredentials.ResolveFirstProviderWebToken("deepseek", _config)
               ?? AccountCredentials.ResolveWebUserToken(null, _config, allowConfigFallback: true);
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
        DsdEmbeddedConfigApplicator.ApplyPatch(_config, patch);

    private object? StoreGet(JsonElement[] args)
    {
        var key = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrEmpty(key)) return null;
        if (key == "config") return BuildDsdApiConfig();
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

    private object[] GetProviders()
    {
        ProviderAvailabilitySync.PruneProvidersWithoutAccounts(_config);
        ProviderAvailabilitySync.NormalizeEnabledFlags(_config);
        _readCache.Remove("providers:getAll");
        return ProviderAvailabilitySync.ListProvidersWithAccounts(_config)
            .Select(p => ApiManagementConsoleMapper.ToUiProvider(p, _config, probeHealth: false))
            .Cast<object>()
            .ToArray();
    }

    private async Task<object> AddProviderAsync(JsonElement[] args)
    {
        var body = args.Length > 0 ? args[0] : default;
        var entry = ApiManagementConsoleMapper.ParseProviderFromUi(body, _config);
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("apiKey", out var k)
            && !string.IsNullOrWhiteSpace(k.GetString()))
            CredentialVault.Set(entry.Id, "api_key", k.GetString()!);
        ApiProviderRegistry.AddOrUpdate(_config, entry);
        await PersistAndSyncAsync();
        return ApiManagementConsoleMapper.ToUiProvider(entry, _config);
    }

    private async Task<object> UpdateProviderAsync(JsonElement[] args)
    {
        string? id = null;
        JsonElement body = default;
        if (args.Length >= 2 && args[0].ValueKind == JsonValueKind.String)
        {
            id = args[0].GetString();
            body = args[1];
        }
        else if (args.Length > 0)
            body = args[0];

        var existing = ApiProviderRegistry.Get(_config, id ?? "")
                     ?? ApiProviderRegistry.Get(_config, body.TryGetProperty("id", out var idEl) ? idEl.GetString() : null);
        if (existing is null)
            throw new InvalidOperationException("供应商不存在");

        var entry = MergeProviderUpdate(existing, body);
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("enabled", out var en)
            && en.ValueKind == JsonValueKind.True)
            ProviderAvailabilitySync.EnsureCanEnable(_config, entry);

        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("apiKey", out var k)
            && !string.IsNullOrWhiteSpace(k.GetString()))
            CredentialVault.Set(entry.Id, "api_key", k.GetString()!);

        ApiProviderRegistry.AddOrUpdate(_config, entry);
        await PersistAndSyncAsync();
        return ApiManagementConsoleMapper.ToUiProvider(entry, _config, probeHealth: false);
    }

    private static ApiProviderEntry MergeProviderUpdate(ApiProviderEntry existing, JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return existing;

        if (body.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
        {
            var name = n.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                existing.DisplayName = name;
        }

        if (body.TryGetProperty("enabled", out var en) && en.ValueKind is JsonValueKind.True or JsonValueKind.False)
            existing.Enabled = en.GetBoolean();

        if (body.TryGetProperty("apiEndpoint", out var ep) && ep.ValueKind == JsonValueKind.String)
        {
            var url = ep.GetString();
            if (!string.IsNullOrWhiteSpace(url))
                existing.BaseUrl = url;
        }

        if (body.TryGetProperty("supportedModels", out var sm) && sm.ValueKind == JsonValueKind.Array)
        {
            var models = new List<string>();
            foreach (var m in sm.EnumerateArray())
            {
                if (m.ValueKind == JsonValueKind.String)
                {
                    var s = m.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        models.Add(s!);
                }
            }

            if (models.Count > 0)
                existing.Models = models;
        }

        return existing;
    }

    private static bool DeleteProvider(JsonElement[] args)
    {
        if (args.Length == 0) return false;
        var body = args[0];
        var id = body.ValueKind switch
        {
            JsonValueKind.String => body.GetString(),
            JsonValueKind.Object when body.TryGetProperty("id", out var el) => el.GetString(),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(id) && ApiProviderRegistry.Delete(id!);
    }

    private static ApiProviderEntry ParseProviderEntry(JsonElement args)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return new ApiProviderEntry { Id = Guid.NewGuid().ToString("N")[..8], DisplayName = "Custom" };

        return new ApiProviderEntry
        {
            Id = args.TryGetProperty("id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("N")[..8] : Guid.NewGuid().ToString("N")[..8],
            DisplayName = args.TryGetProperty("name", out var n) ? n.GetString() ?? "Custom" : "Custom",
            Kind = args.TryGetProperty("kind", out var k) ? k.GetString() ?? ApiProviderKinds.OpenAiCompatible : ApiProviderKinds.OpenAiCompatible,
            RouteMode = args.TryGetProperty("routeMode", out var r) ? r.GetString() ?? ApiRouteModes.DirectApi : ApiRouteModes.DirectApi,
            BaseUrl = args.TryGetProperty("baseUrl", out var u) ? u.GetString() ?? "" : "",
            Enabled = !args.TryGetProperty("enabled", out var e) || e.GetBoolean()
        };
    }

    private object[] GetBuiltinProviders() => BuiltinProviderCatalog.ToUiBuiltinList(_config);

    private object CheckProviderStatus(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        var entry = ApiProviderRegistry.Get(_config, providerId);
        if (entry is null)
            return new { providerId, status = "offline", latency = (int?)null, error = "未知供应商" };

        var status = ApiManagementConsoleMapper.ResolveStatus(entry, _config, probeHealth: false);
        return new
        {
            providerId,
            status,
            latency = status == "online" ? 48 : (int?)null,
            error = status == "online" ? null : "请先添加至少一个有效账户"
        };
    }

    private object CheckAllProviderStatus()
    {
        ProviderAvailabilitySync.PruneProvidersWithoutAccounts(_config);
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ProviderAvailabilitySync.ListProvidersWithAccounts(_config))
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(p.Id));
            dict[p.Id] = CheckProviderStatus([doc.RootElement]);
        }

        return dict;
    }

    private object GetEffectiveModels(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        DsdOpenAiCompat.EnsureDefaultMappings(_config);
        return ProviderModelCatalog.GetEffectiveModels(_config, providerId);
    }

    private async Task<object> SyncProviderModelsAsync(JsonElement[] args, CancellationToken ct)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        var result = await ProviderModelCatalog.SyncModelsAsync(_config, providerId ?? "deepseek", ct);
        if (result.Success)
            await PersistAndSyncAsync();
        return new
        {
            success = result.Success,
            supportedModels = result.SupportedModels,
            modelMappings = result.ModelMappings,
            error = result.Error
        };
    }

    private async Task<object> UpdateProviderModelsAsync(JsonElement[] args, CancellationToken ct)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        var result = await ProviderModelCatalog.UpdateModelsAsync(_config, providerId ?? "deepseek", ct);
        if (result.Success)
            await PersistAndSyncAsync();
        return new { success = result.Success, modelsCount = result.ModelsCount, error = result.Error };
    }

    private Task<object> AddCustomModelAsync(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        if (string.IsNullOrWhiteSpace(providerId))
            return Task.FromResult<object>(new { success = false, models = Array.Empty<object>(), error = "Invalid provider" });

        try
        {
            if (args.Length < 2 || args[1].ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Invalid model data");

            var body = args[1];
            var model = new DsdUserModelOverridesStore.CustomModelRecord
            {
                DisplayName = body.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
                ActualModelId = body.TryGetProperty("actualModelId", out var am) ? am.GetString() ?? "" : ""
            };
            var models = DsdUserModelOverridesStore.AddCustomModel(providerId, model);
            return Task.FromResult<object>(new { success = true, models, error = (string?)null });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                models = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }

    private Task<object> RemoveCustomModelAsync(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        var modelName = args.Length > 1 ? args[1].GetString() : null;
        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelName))
        {
            return Task.FromResult<object>(new
            {
                success = false,
                models = Array.Empty<object>(),
                error = "Invalid parameters"
            });
        }

        try
        {
            var models = DsdUserModelOverridesStore.RemoveModel(_config, providerId, modelName);
            return Task.FromResult<object>(new { success = true, models, error = (string?)null });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                models = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }

    private Task<object> ResetModelsAsync(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : "deepseek";
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Task.FromResult<object>(new
            {
                success = false,
                models = Array.Empty<object>(),
                error = "Invalid provider"
            });
        }

        try
        {
            var models = DsdUserModelOverridesStore.ResetProvider(providerId);
            return Task.FromResult<object>(new { success = true, models, error = (string?)null });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                models = Array.Empty<object>(),
                error = ex.Message
            });
        }
    }

    private object[] GetAccounts(JsonElement[] args)
    {
        var includeCreds = args.Length > 0 && args[0].ValueKind == JsonValueKind.True;
        return ProviderAccountStore.Load()
            .Select(a => ApiManagementConsoleMapper.ToUiAccount(a, includeCreds))
            .Cast<object>()
            .ToArray();
    }

    private object? GetAccountById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var include = args.Length > 1 && args[1].ValueKind == JsonValueKind.True;

        var rec = ProviderAccountStore.FindById(id);
        return rec is null ? null : ApiManagementConsoleMapper.ToUiAccount(rec, include);
    }

    private async Task<object> AddAccountAsync(JsonElement[] args)
    {
        var body = args.Length > 0 ? args[0] : default;
        if (body.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("无效的账户数据");

        var providerId = body.TryGetProperty("providerId", out var pid) ? pid.GetString() ?? "" : "";
        var name = body.TryGetProperty("name", out var n) ? n.GetString() ?? "Account" : "Account";
        var creds = new Dictionary<string, string>(StringComparer.Ordinal);
        if (body.TryGetProperty("credentials", out var c) && c.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in c.EnumerateObject())
                creds[prop.Name] = prop.Value.GetString() ?? "";
        }

        var email = body.TryGetProperty("email", out var em) ? em.GetString() : null;
        int? dailyLimit = null;
        if (body.TryGetProperty("dailyLimit", out var dl) && dl.ValueKind == JsonValueKind.Number)
            dailyLimit = dl.TryGetInt32(out var limitVal) ? limitVal : null;

        ProviderAvailabilitySync.EnsureProviderRegistryEntry(_config, providerId);
        var rec = ProviderAccountStore.AddOrUpdate(providerId, name, creds, email, dailyLimit);
        OAuthAccountProvisioner.SyncDeepSeekWebToken(_config, creds);
        if (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
            await ProviderModelCatalog.SyncModelsAsync(_config, providerId);
        var providerEntry = ApiProviderRegistry.Get(_config, providerId);
        if (providerEntry is not null
            && ProviderAvailabilitySync.AccountHasCredentials(providerEntry, rec, _config)
            && !providerEntry.Enabled)
        {
            providerEntry.Enabled = true;
            ApiProviderRegistry.AddOrUpdate(_config, providerEntry);
        }

        DsdAppLogStore.Instance.Add("info", $"创建账户: {rec.Name}",
            new Dictionary<string, string> { ["accountId"] = rec.Id, ["providerId"] = rec.ProviderId });
        await PersistAndSyncAsync();
        return ApiManagementConsoleMapper.ToUiAccount(rec, includeCredentials: false);
    }

    private object[] GetAccountsByProvider(JsonElement[] args)
    {
        var providerId = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(providerId))
            return [];

        return ListAccountsForProvider(providerId, includeCredentials: false).ToArray();
    }

    private IEnumerable<object> ListAccountsForProvider(string providerId, bool includeCredentials) =>
        ProviderAccountStore.ByProvider(providerId)
            .Select(a => ApiManagementConsoleMapper.ToUiAccount(a, includeCredentials))
            .Cast<object>();

    private async Task<object?> UpdateAccountAsync(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("账户 ID 无效");

        var updates = args.Length > 1 ? args[1] : default;
        if (updates.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("无效的账户更新数据");

        var rec = ProviderAccountStore.Update(id, existing =>
        {
            if (updates.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                existing.Name = nameEl.GetString() ?? existing.Name;
            if (updates.TryGetProperty("email", out var emailEl) && emailEl.ValueKind == JsonValueKind.String)
                existing.Email = emailEl.GetString() ?? "";
            if (updates.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                existing.Status = statusEl.GetString() ?? existing.Status;
            if (updates.TryGetProperty("dailyLimit", out var limitEl))
            {
                existing.DailyLimit = limitEl.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number when limitEl.TryGetInt32(out var n) => n,
                    _ => existing.DailyLimit
                };
            }

            if (updates.TryGetProperty("credentials", out var credsEl) && credsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in credsEl.EnumerateObject())
                {
                    var val = prop.Value.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(val) && !val.StartsWith("••••", StringComparison.Ordinal))
                        existing.Credentials[prop.Name] = val;
                }
            }
        });

        if (rec is null)
            throw new InvalidOperationException("账户不存在");

        OAuthAccountProvisioner.SyncDeepSeekWebToken(_config, rec.Credentials);
        if (string.Equals(rec.ProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
            await ProviderModelCatalog.SyncModelsAsync(_config, rec.ProviderId);

        DsdAppLogStore.Instance.Add("info", $"更新账户: {rec.Name}",
            new Dictionary<string, string> { ["accountId"] = rec.Id, ["providerId"] = rec.ProviderId });
        await PersistAndSyncAsync();
        return ApiManagementConsoleMapper.ToUiAccount(rec, includeCredentials: false);
    }

    private async Task<bool> DeleteAccountAsync(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var existing = ProviderAccountStore.FindById(id);
        if (!ProviderAccountStore.Delete(id))
            return false;
        if (existing is not null)
        {
            DsdAppLogStore.Instance.Add("info", $"删除账户: {existing.Name}",
                new Dictionary<string, string> { ["accountId"] = id, ["providerId"] = existing.ProviderId });
            if (ProviderAccountStore.ByProvider(existing.ProviderId).Count == 0)
                ProviderAvailabilitySync.OnLastAccountRemoved(_config, existing.ProviderId);
        }

        await PersistAndSyncAsync();
        return true;
    }

    private async Task<object> DuplicateProviderAsync(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        var newName = args.Length > 1 ? args[1].GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("供应商 ID 无效");

        var entry = ApiProviderRegistry.Duplicate(_config, id, newName);
        await PersistAndSyncAsync();
        DsdAppLogStore.Instance.Add("info", $"复制供应商: {entry.DisplayName}",
            new Dictionary<string, string> { ["providerId"] = entry.Id });
        return ApiManagementConsoleMapper.ToUiProvider(entry, _config);
    }

    private async Task<object?> GetAccountCreditsAsync(JsonElement[] args, CancellationToken ct)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var rec = ProviderAccountStore.FindById(id);
        if (rec is null || !string.Equals(rec.ProviderId, "minimax", StringComparison.OrdinalIgnoreCase))
            return null;

        var credits = await MiniMaxAccountApi.GetCreditsAsync(rec, ct);
        return credits is null
            ? null
            : new
            {
                totalCredits = credits.TotalCredits,
                usedCredits = credits.UsedCredits,
                remainingCredits = credits.RemainingCredits,
                expiresAt = credits.ExpiresAt
            };
    }

    private async Task<object> ClearAccountChatsAsync(JsonElement[] args, CancellationToken ct)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            return new { success = false, error = "账户不存在" };

        var rec = ProviderAccountStore.FindById(id);
        if (rec is null)
            return new { success = false, error = "账户不存在" };

        var (success, error) = await ProviderChatClearService.ClearAllChatsAsync(rec, _config, ct);
        if (success)
        {
            DsdAppLogStore.Instance.Add("info", $"清除对话记录: {rec.Name}",
                new Dictionary<string, string> { ["accountId"] = rec.Id, ["providerId"] = rec.ProviderId });
        }

        return new { success, error };
    }

    private static object[] AppLogsGet(JsonElement[] args)
    {
        DsdAppLogStore.AppLogFilter? filter = null;
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
        {
            var el = args[0];
            filter = new DsdAppLogStore.AppLogFilter
            {
                Level = el.TryGetProperty("level", out var lv) ? lv.GetString() : null,
                Keyword = el.TryGetProperty("keyword", out var kw) ? kw.GetString() : null,
                StartTime = el.TryGetProperty("startTime", out var st) && st.TryGetInt64(out var s) ? s : null,
                EndTime = el.TryGetProperty("endTime", out var et) && et.TryGetInt64(out var e) ? e : null,
                Limit = el.TryGetProperty("limit", out var lim) && lim.TryGetInt32(out var l) ? l : null,
                Offset = el.TryGetProperty("offset", out var off) && off.TryGetInt32(out var o) ? o : null
            };
        }

        return DsdAppLogStore.Instance.GetLogs(filter).Cast<object>().ToArray();
    }

    private static object? AppLogsGetById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        return string.IsNullOrWhiteSpace(id) ? null : DsdAppLogStore.Instance.GetById(id);
    }

    private static object? AppLogsClear()
    {
        DsdAppLogStore.Instance.Clear();
        return null;
    }

    private static int ParseTrendDays(JsonElement[] args) =>
        args.Length > 0 && args[0].ValueKind == JsonValueKind.Number && args[0].TryGetInt32(out var d)
            ? d
            : 7;

    private static string ParseExportFormat(JsonElement[] args) =>
        args.Length > 0 && args[0].ValueKind == JsonValueKind.String
            ? args[0].GetString() ?? "json"
            : "json";

    private static object[] GetAccountTrend(JsonElement[] args)
    {
        var accountId = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(accountId))
            return [];

        var days = 7;
        if (args.Length > 1 && args[1].ValueKind == JsonValueKind.Number && args[1].TryGetInt32(out var d))
            days = d;

        return RequestLogAccountStats.GetTrend(accountId, days)
            .Select(p => (object)new { date = p.Date, total = p.Total, info = p.Info, warn = p.Warn, error = p.Error })
            .ToArray();
    }

    private static string? ResolveSessionAccountId() =>
        ProviderAccountStore.ByProvider("deepseek")
            .FirstOrDefault(a => a.Status == "active")?.Id;

    private async Task<object> ValidateAccountAsync(JsonElement[] args, CancellationToken ct)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return false;

        var rec = ProviderAccountStore.FindById(id);
        if (rec is null)
            return false;

        var check = await ProviderCredentialValidator.ValidateAccountAsync(rec, _config, ct);
        ProviderAccountStore.Update(rec.Id, r =>
        {
            r.Status = check.Valid ? "active" : "error";
            r.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (check.Valid && !string.IsNullOrWhiteSpace(check.Email))
                r.Email = check.Email;
        });
        await PersistAndSyncAsync();

        if (check.Valid)
            DsdAppLogStore.Instance.Add("info", $"验证账户成功: {rec.Name}",
                new Dictionary<string, string> { ["accountId"] = rec.Id, ["providerId"] = rec.ProviderId });

        return check.Valid;
    }

    private async Task<object> ValidateTokenAsync(JsonElement[] args, CancellationToken ct)
    {
        if (!TryParseValidateTokenArgs(args, out var providerId, out var credentials))
        {
            return new
            {
                valid = false,
                error = "参数无效",
                userInfo = (object?)null
            };
        }

        providerId = string.IsNullOrWhiteSpace(providerId) ? "deepseek" : providerId.Trim();

        if (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
            return await ValidateDeepSeekTokenAsync(credentials, ct);

        var outcome = await ProviderCredentialValidator.ValidateProviderCredentialsAsync(
            providerId, credentials, ct);
        return new
        {
            valid = outcome.Valid,
            error = outcome.Valid ? null : outcome.Error ?? "凭证无效",
            userInfo = outcome.Valid
                ? new { name = outcome.Name ?? providerId, email = outcome.Email ?? "" }
                : null
        };
    }

    private async Task<object> ValidateDeepSeekTokenAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken ct)
    {
        var token = ResolveCredentialValue(credentials, "token");
        if (string.IsNullOrWhiteSpace(token))
        {
            return new
            {
                valid = false,
                error = "请填写 DeepSeek 用户 Token",
                userInfo = (object?)null
            };
        }

        var check = await DeepSeekWebTokenValidator.ValidateAsync(token, ct);
        return new
        {
            valid = check.Valid,
            error = check.Valid ? null : check.Error ?? "Token 无效",
            userInfo = check.Valid
                ? new { name = check.Name ?? "DeepSeek", email = check.Email ?? "" }
                : null
        };
    }

    private static object ValidateBuiltinCredentials(
        string providerId,
        IReadOnlyDictionary<string, string> credentials)
    {
        var meta = BuiltinProviderCatalog.Find(providerId);
        if (meta is null)
        {
            return new
            {
                valid = false,
                error = $"未知供应商：{providerId}",
                userInfo = (object?)null
            };
        }

        var missing = new List<string>();
        foreach (var field in meta.CredentialFields)
        {
            if (!field.Required)
                continue;
            if (string.IsNullOrWhiteSpace(ResolveCredentialValue(credentials, field.Name)))
                missing.Add(field.Label);
        }

        if (missing.Count > 0)
        {
            return new
            {
                valid = false,
                error = $"请填写必填字段：{string.Join("、", missing)}",
                userInfo = (object?)null
            };
        }

        return new
        {
            valid = true,
            error = (string?)null,
            userInfo = new
            {
                name = meta.Name,
                email = "",
                note = "DeepSeek Desktop 已校验凭证格式；连接性在保存账户后由路由探测。"
            }
        };
    }

    private static bool TryParseValidateTokenArgs(
        JsonElement[] args,
        out string? providerId,
        out Dictionary<string, string> credentials)
    {
        providerId = null;
        credentials = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (args.Length >= 2)
        {
            if (args[0].ValueKind == JsonValueKind.String)
                providerId = args[0].GetString();
            MergeCredentials(credentials, args[1]);
            return true;
        }

        if (args.Length == 1 && args[0].ValueKind == JsonValueKind.Object)
        {
            var root = args[0];
            if (root.TryGetProperty("providerId", out var pid))
                providerId = pid.GetString();
            if (root.TryGetProperty("credentials", out var creds))
                MergeCredentials(credentials, creds);
            if (credentials.Count == 0 && root.TryGetProperty("token", out var tok))
                credentials["token"] = tok.GetString() ?? "";
            return true;
        }

        return args.Length > 0;
    }

    private static void MergeCredentials(Dictionary<string, string> target, JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                    target[prop.Name] = prop.Value.GetString() ?? "";
            }
            return;
        }

        if (el.ValueKind == JsonValueKind.String)
            target["token"] = el.GetString() ?? "";
    }

    private static string? ResolveCredentialValue(IReadOnlyDictionary<string, string> credentials, string name)
    {
        if (credentials.TryGetValue(name, out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        foreach (var kv in credentials)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(kv.Value))
                return kv.Value.Trim();
        }

        return null;
    }

    private static string? ExtractTokenFromArgs(JsonElement[] args)
    {
        if (!TryParseValidateTokenArgs(args, out _, out var credentials))
            return null;
        return ResolveCredentialValue(credentials, "token");
    }

    private object SessionConfig()
    {
        var cfg = DsdSessionConfigStore.Get();
        return new
        {
            sessionTimeout = cfg.SessionTimeout,
            maxMessagesPerSession = cfg.MaxMessagesPerSession,
            deleteAfterTimeout = cfg.DeleteAfterTimeout,
            maxSessionsPerAccount = cfg.MaxSessionsPerAccount,
            mode = string.Equals(_config.DsdApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase)
                ? "multi"
                : "single"
        };
    }

    private async Task<object> SessionUpdateConfig(JsonElement[] args)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
        {
            var el = args[0];
            DsdSessionConfigStore.Update(cfg =>
            {
                if (el.TryGetProperty("sessionTimeout", out var t) && t.TryGetInt32(out var minutes))
                    cfg.SessionTimeout = Math.Max(1, minutes);
                if (el.TryGetProperty("maxMessagesPerSession", out var max) && max.TryGetInt32(out var maxMsg))
                    cfg.MaxMessagesPerSession = Math.Max(1, maxMsg);
                if (el.TryGetProperty("deleteAfterTimeout", out var del))
                    cfg.DeleteAfterTimeout = del.GetBoolean();
                if (el.TryGetProperty("maxSessionsPerAccount", out var msa) && msa.TryGetInt32(out var maxSessions))
                    cfg.MaxSessionsPerAccount = Math.Max(1, maxSessions);
            });
            await ApplyConfigPatchAsync(el);
        }

        return SessionConfig();
    }

    private void SyncWebSessionsToRecordStore()
    {
        var accountId = ResolveSessionAccountId() ?? "";
        foreach (var s in _localApi.ListSessions())
        {
            DsdApiSessionRecordStore.UpsertWebBridgeSession(
                s.ClientSessionId,
                s.WebSessionId,
                "deepseek",
                accountId,
                s.LastUsedAt,
                s.MessageCount);
        }
    }

    private object[] ListSessions(bool activeOnly = false)
    {
        SyncWebSessionsToRecordStore();
        var sessions = activeOnly
            ? DsdApiSessionRecordStore.GetActive()
            : DsdApiSessionRecordStore.GetAll();
        return sessions.Select(DsdApiSessionRecordStore.ToUi).Cast<object>().ToArray();
    }

    private object[] SessionsByAccount(JsonElement[] args)
    {
        SyncWebSessionsToRecordStore();
        var accountId = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(accountId)) return Array.Empty<object>();
        return DsdApiSessionRecordStore.GetByAccountId(accountId)
            .Select(DsdApiSessionRecordStore.ToUi)
            .Cast<object>()
            .ToArray();
    }

    private object[] SessionsByProvider(JsonElement[] args)
    {
        SyncWebSessionsToRecordStore();
        var providerId = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(providerId)) return Array.Empty<object>();
        return DsdApiSessionRecordStore.GetByProviderId(providerId)
            .Select(DsdApiSessionRecordStore.ToUi)
            .Cast<object>()
            .ToArray();
    }

    private object? SessionById(JsonElement[] args)
    {
        SyncWebSessionsToRecordStore();
        var id = args.Length > 0 ? args[0].GetString() : null;
        var hit = DsdApiSessionRecordStore.GetById(id ?? "");
        return hit is null ? null : DsdApiSessionRecordStore.ToUi(hit);
    }

    private object SessionDelete(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrEmpty(id)) return false;
        _localApi.DeleteSession(id);
        return DsdApiSessionRecordStore.Delete(id);
    }

    private object? SessionClearAll()
    {
        foreach (var s in _localApi.ListSessions())
            _localApi.DeleteSession(s.ClientSessionId);
        DsdApiSessionRecordStore.ClearAll();
        return null;
    }

    private int CleanExpiredSessions()
    {
        _localApi.CleanExpiredSessions();
        return DsdApiSessionRecordStore.CleanExpired();
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

    private object ContextManagementConfig()
    {
        var cfg = DsdContextManagementConfigStore.Get();
        var sw = cfg.Strategies.SlidingWindow;
        var tl = cfg.Strategies.TokenLimit;
        var sum = cfg.Strategies.Summary;
        return new
        {
            enabled = cfg.Enabled,
            strategies = new
            {
                slidingWindow = new
                {
                    enabled = sw.Enabled,
                    maxMessages = sw.MaxMessages > 0 ? sw.MaxMessages : _config.DsdApiMaxMessagesPerSession
                },
                tokenLimit = new { enabled = tl.Enabled, maxTokens = tl.MaxTokens > 0 ? tl.MaxTokens : 8000 },
                summary = new
                {
                    enabled = sum.Enabled,
                    keepRecentMessages = sum.KeepRecentMessages > 0 ? sum.KeepRecentMessages : 20,
                    customPrompt = sum.CustomPrompt
                }
            },
            executionOrder = cfg.ExecutionOrder.Count > 0
                ? cfg.ExecutionOrder.ToArray()
                : new[] { "slidingWindow", "tokenLimit", "summary" }
        };
    }

    private async Task<object> ContextManagementUpdateAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length > 0 && args[0].ValueKind == JsonValueKind.Object)
        {
            var el = args[0];
            DsdContextManagementConfigStore.Update(cfg => DsdContextManagementConfigStore.ApplyJsonPatch(cfg, el));

            using var wrap = JsonDocument.Parse($"{{\"contextManagement\":{el.GetRawText()}}}");
            DsdEmbeddedConfigApplicator.ApplyPatch(_config, wrap.RootElement);
            await PersistAndSyncAsync(ct);
        }

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

    private async Task<object> OAuthInAppLoginAsync(JsonElement[] args, CancellationToken ct)
    {
        var (providerId, providerType) = ParseOAuthArgs(args);
        var baseResult = await _oauthLogin.StartAsync(
            providerId,
            providerType,
            ct,
            (status, message) => EmitOAuthProgress(status, message));
        return await EnrichOAuthWithAccountAsync(baseResult, providerId, providerType, ct);
    }

    private object CancelOAuthInAppLogin()
    {
        _oauthLogin.Cancel();
        return null!;
    }

    private async Task<object> OAuthBrowserLoginAsync(JsonElement[] args, CancellationToken ct)
    {
        var (providerId, providerType) = ParseOAuthArgs(args);
        _oauthBrowserLoginPending = true;
        EmitOAuthProgress("pending", "Opening browser...");
        var result = await DsdOAuthBrowserLogin.StartLoginAsync(providerId, providerType);
        EmitOAuthProgress("pending", "Please log in via browser and enter credentials manually");
        return result;
    }

    private string OAuthGetStatus()
    {
        if (_oauthLogin.IsOpen || _oauthBrowserLoginPending)
            return "pending";
        return "idle";
    }

    private static object? OAuthCallback(JsonElement[] args) => null;

    private object CancelOAuthLogin()
    {
        _oauthBrowserLoginPending = false;
        _oauthLogin.Cancel();
        return null!;
    }

    private async Task<object?> OAuthRefreshTokenAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].ValueKind != JsonValueKind.Object)
            return new { success = false, error = "无效的刷新参数" };

        var body = args[0];
        var providerId = body.TryGetProperty("providerId", out var p) ? p.GetString() ?? "" : "";
        var providerType = body.TryGetProperty("providerType", out var t) ? t.GetString() ?? providerId : providerId;
        var credentials = ParseCredentialDict(body.TryGetProperty("credentials", out var c) ? c : default);

        var key = (providerId + providerType).ToLowerInvariant();
        if (key.Contains("glm", StringComparison.Ordinal))
        {
            var refreshed = await GlmTokenRefresh.RefreshCredentialsAsync(credentials, ct);
            if (refreshed is null)
                return null;
            var refresh = refreshed.TryGetValue("refresh_token", out var rt) ? rt : "";
            var access = refreshed.TryGetValue("access_token", out var at) ? at : "";
            return new
            {
                type = "refresh",
                value = access,
                refreshToken = refresh,
                extra = refreshed
            };
        }

        return credentials.Count > 0
            ? new { type = "token", value = credentials.Values.First(), extra = credentials }
            : null;
    }

    private static Dictionary<string, string> ParseCredentialDict(JsonElement el)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (el.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in el.EnumerateObject())
        {
            var val = prop.Value.GetString();
            if (!string.IsNullOrWhiteSpace(val))
                dict[prop.Name] = val;
        }

        return dict;
    }

    private static object[] PromptsGetAll() =>
        DsdSystemPromptStore.GetAll().Select(DsdSystemPromptStore.ToUi).Cast<object>().ToArray();

    private static object[] PromptsGetBuiltin() =>
        DsdSystemPromptStore.GetBuiltin().Select(DsdSystemPromptStore.ToUi).Cast<object>().ToArray();

    private static object[] PromptsGetCustom() =>
        DsdSystemPromptStore.GetCustom().Select(DsdSystemPromptStore.ToUi).Cast<object>().ToArray();

    private static object? PromptsGetById(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        var rec = DsdSystemPromptStore.GetById(id ?? "");
        return rec is null ? null : DsdSystemPromptStore.ToUi(rec);
    }

    private static object[] PromptsGetByType(JsonElement[] args)
    {
        var type = args.Length > 0 ? args[0].GetString() ?? "general" : "general";
        return DsdSystemPromptStore.GetByType(type).Select(DsdSystemPromptStore.ToUi).Cast<object>().ToArray();
    }

    private static object PromptsAdd(JsonElement[] args)
    {
        if (args.Length == 0 || args[0].ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("无效的系统提示词数据");
        var draft = ParsePromptDraft(args[0]);
        var rec = DsdSystemPromptStore.Add(draft);
        return DsdSystemPromptStore.ToUi(rec);
    }

    private static object? PromptsUpdate(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || args.Length < 2 || args[1].ValueKind != JsonValueKind.Object)
            return null;

        var updates = args[1];
        var rec = DsdSystemPromptStore.Update(id, r =>
        {
            if (updates.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                r.Name = n.GetString() ?? r.Name;
            if (updates.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String)
                r.Description = d.GetString() ?? r.Description;
            if (updates.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String)
                r.Prompt = p.GetString() ?? r.Prompt;
            if (updates.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String)
                r.Type = ty.GetString() ?? r.Type;
            if (updates.TryGetProperty("emoji", out var em))
                r.Emoji = em.ValueKind == JsonValueKind.Null ? null : em.GetString();
        });
        return rec is null ? null : DsdSystemPromptStore.ToUi(rec);
    }

    private static bool PromptsDelete(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        return !string.IsNullOrWhiteSpace(id) && DsdSystemPromptStore.Delete(id!);
    }

    private static DsdSystemPromptStore.SystemPromptRecord ParsePromptDraft(JsonElement el) => new()
    {
        Name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
        Description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
        Prompt = el.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "",
        Type = el.TryGetProperty("type", out var ty) ? ty.GetString() ?? "general" : "general",
        Emoji = el.TryGetProperty("emoji", out var em) ? em.GetString() : null
    };

    private static (string ProviderId, string ProviderType) ParseOAuthArgs(JsonElement[] args)
    {
        var providerId = "deepseek";
        var providerType = "deepseek";
        if (args.Length == 0)
            return (providerId, providerType);

        var el = args[0];
        if (el.ValueKind == JsonValueKind.String)
        {
            providerId = el.GetString() ?? providerId;
            providerType = args.Length > 1 && args[1].ValueKind == JsonValueKind.String
                ? args[1].GetString() ?? providerId
                : providerId;
            return (providerId, providerType);
        }

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("providerId", out var pid) && pid.ValueKind == JsonValueKind.String)
                providerId = pid.GetString() ?? providerId;
            if (el.TryGetProperty("providerType", out var pt) && pt.ValueKind == JsonValueKind.String)
                providerType = pt.GetString() ?? providerId;
            else
                providerType = providerId;
        }

        return (providerId, providerType);
    }

    private async Task<object> OAuthLoginWithTokenAsync(JsonElement[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].ValueKind != JsonValueKind.Object)
            return new { success = false, error = "invalid payload" };

        var payload = args[0];
        var providerId = payload.TryGetProperty("providerId", out var pid) && pid.ValueKind == JsonValueKind.String
            ? pid.GetString() ?? "deepseek"
            : "deepseek";
        var providerType = payload.TryGetProperty("providerType", out var pt) && pt.ValueKind == JsonValueKind.String
            ? pt.GetString() ?? providerId
            : providerId;

        var rawCreds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (payload.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(tok.GetString()))
            rawCreds["token"] = tok.GetString()!.Trim();

        if (payload.TryGetProperty("realUserID", out var rid) && rid.ValueKind == JsonValueKind.String)
            rawCreds["realUserID"] = rid.GetString() ?? "";
        if (payload.TryGetProperty("mimoUserId", out var muid) && muid.ValueKind == JsonValueKind.String)
            rawCreds["user_id"] = muid.GetString() ?? "";
        if (payload.TryGetProperty("mimoPhToken", out var mph) && mph.ValueKind == JsonValueKind.String)
            rawCreds["ph_token"] = mph.GetString() ?? "";

        if (rawCreds.Count == 0)
            return new { success = false, error = "缺少 token" };

        var mapped = DsdOAuthCredentialMapper.Map(providerId, rawCreds);
        var accountName = payload.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
            ? nameEl.GetString()
            : null;

        var provision = await OAuthAccountProvisioner.ProvisionAsync(
            providerId, mapped, _config, accountName, email: null, credentialsAlreadyMapped: true, ct);
        await PersistAndSyncAsync(ct);

        if (!provision.Success)
        {
            return new
            {
                success = false,
                providerId,
                providerType,
                error = provision.Error ?? "创建账户失败"
            };
        }

        DsdAppLogStore.Instance.Add("info", $"OAuth Token 登录并创建账户: {provision.Account!.Name}",
            new Dictionary<string, string>
            {
                ["accountId"] = provision.Account.Id,
                ["providerId"] = providerId
            });

        return new
        {
            success = true,
            providerId,
            providerType,
            credentials = mapped,
            accountInfo = new { name = provision.Account.Name, email = provision.Account.Email },
            account = ApiManagementConsoleMapper.ToUiAccount(provision.Account, includeCredentials: false),
            error = (string?)null
        };
    }

    private async Task<object> EnrichOAuthWithAccountAsync(
        object baseResult,
        string providerId,
        string providerType,
        CancellationToken ct)
    {
        _ = ct;
        _ = providerId;
        _ = providerType;
        // OAuth 登录只回填凭证；账户在用户确认「添加账户/供应商」时创建，避免重复写入。
        return baseResult;
    }

    private string ExportProvider(JsonElement[] args)
    {
        var id = args.Length > 0 ? args[0].GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("供应商 ID 无效");
        return ProviderImportExport.Export(_config, id);
    }

    private async Task<object> ImportProviderAsync(JsonElement[] args)
    {
        var json = args.Length > 0 && args[0].ValueKind == JsonValueKind.String
            ? args[0].GetString()
            : null;
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("JSON 数据无效");

        var entry = ProviderImportExport.Import(_config, json);
        await PersistAndSyncAsync();
        DsdAppLogStore.Instance.Add("info", $"导入供应商: {entry.DisplayName}",
            new Dictionary<string, string> { ["providerId"] = entry.Id });
        return ApiManagementConsoleMapper.ToUiProvider(entry, _config);
    }
}
