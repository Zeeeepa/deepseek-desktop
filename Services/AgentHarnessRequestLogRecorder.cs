using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services;

/// <summary>将 Harness / 网页桥 LLM 调用写入 DsdApiRequestLogStore，供仪表盘请求趋势使用。</summary>
public static class AgentHarnessRequestLogRecorder
{
    public static void Register()
    {
        AgentRequestTelemetry.OnCompleted = Record;
    }

    private static void Record(AgentRequestTelemetryEvent evt)
    {
        try
        {
            if (string.Equals(evt.Channel, "openai-api", StringComparison.OrdinalIgnoreCase)
                && IsLocalOpenAiServer(evt.Config))
                return;

            var resolution = ApiRouteResolver.Resolve(evt.Config, webBridge: null, evt.Config.AgentDefaultProviderId, evt.Model);
            var success = evt.Error is null && evt.Result is not null;
            var preview = evt.Result?.Content;
            if (string.IsNullOrWhiteSpace(preview))
                preview = evt.Result?.ReasoningContent;

            DsdApiRequestLogStore.Instance.Add(new DsdApiRequestLogStore.RequestLogDraft
            {
                Success = success,
                StatusCode = success ? 200 : 500,
                ResponseStatus = success ? 200 : 500,
                Method = "POST",
                Url = evt.Channel switch
                {
                    "embedded-web" => "/agent/web-chat",
                    _ => "/v1/chat/completions"
                },
                Model = evt.Model,
                ActualModel = evt.Result?.Model ?? evt.Model,
                ProviderId = resolution.Provider.Id,
                ProviderName = resolution.Provider.DisplayName,
                AccountId = resolution.Account?.Id ?? "embedded",
                AccountName = resolution.Account?.Name
                    ?? resolution.Provider.DisplayName
                    ?? "DeepSeek Desktop",
                UserInput = null,
                WebSearch = evt.WebSearch,
                ResponsePreview = preview,
                LatencyMs = evt.LatencyMs,
                IsStream = evt.IsStream,
                ErrorMessage = evt.Error?.Message
            });
        }
        catch
        {
            // diagnostics only
        }
    }

    private static bool IsLocalOpenAiServer(AppConfig config)
    {
        var url = AgentChatClientFactory.ResolveBaseUrl(config);
        if (string.IsNullOrWhiteSpace(url))
            return false;
        var local = InternalChatChannel.GetExternalApiBaseUrl(config).TrimEnd('/');
        var norm = url.Trim().TrimEnd('/');
        if (norm.StartsWith(local, StringComparison.OrdinalIgnoreCase))
            return true;
        var port = InternalChatChannel.ResolveExternalApiPort(config);
        return norm.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
               && norm.Contains(":" + port, StringComparison.Ordinal);
    }
}
