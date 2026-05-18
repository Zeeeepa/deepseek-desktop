using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 与 <see href="https://chat2api-doc.vercel.app/zh/docs/">Chat2API 文档</see> 对齐的请求解析、模型映射与会话选项。
/// </summary>
public static class Chat2ApiCompat
{
    public sealed class CompletionRequest
    {
        public string RequestedModel { get; init; } = DefaultModel;
        public string ResolvedModel { get; init; } = DefaultModel;
        public bool Stream { get; init; }
        public bool WebSearch { get; init; }
        public bool Thinking { get; init; }
        public string? SessionId { get; init; }
        public string? WebChatSessionId { get; init; }
        public List<ChatMessage> Messages { get; init; } = new();
        public List<string> RefFileIds { get; init; } = new();
    }

    public const string DefaultModel = "DeepSeek-V3.2";

    /// <summary>对外 /v1/models 仅暴露 DeepSeek 模型（客户端只使用 DeepSeek）。</summary>
    /// <summary>官方 API 文档：<see href="https://api-docs.deepseek.com/zh-cn/"/></summary>
    public const string OfficialApiDocsUrl = "https://api-docs.deepseek.com/zh-cn/";

    private static readonly string[] DeepSeekModelIds =
    {
        DefaultModel,
        "DeepSeek-R1",
        "DeepSeek-Search",
        "DeepSeek-R1-Search",
        "deepseek-chat",
        "deepseek-reasoner",
        "deepseek-web",
        "deepseek-v4-pro",
        "deepseek-v4-flash",
        "DeepSeek-V3.2-Think",
        "DeepSeek-V3.2-Search",
        "DeepSeek-V3.2-Think-Search",
    };

    private static readonly Dictionary<string, string> BuiltinMappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek-v4-pro"] = DefaultModel,
            ["deepseek-v4-flash"] = DefaultModel,
            ["deepseek-chat"] = DefaultModel,
            ["deepseek-reasoner"] = "DeepSeek-R1",
            ["deepseek-web"] = DefaultModel,
            ["DeepSeek-V3.2-Think"] = DefaultModel,
            ["DeepSeek-V3.2-Search"] = DefaultModel,
            ["DeepSeek-V3.2-Think-Search"] = DefaultModel,
            ["DeepSeek-R1-Search"] = "DeepSeek-R1",
            ["DeepSeek-Search"] = "DeepSeek-Search",
        };

    public static void EnsureDefaultMappings(AppConfig config)
    {
        if (config.ModelMappings.Count > 0) return;
        config.ModelMappings = BuiltinMappings.Select(kv => new ModelMappingEntry
        {
            RequestModel = kv.Key,
            ActualModel = kv.Value
        }).ToList();
    }

    public static CompletionRequest ParseCompletion(
        string bodyText,
        System.Net.HttpListenerRequest httpRequest,
        AppConfig config,
        Chat2ApiSessionStore sessions)
    {
        EnsureDefaultMappings(config);
        using var doc = JsonDocument.Parse(bodyText);
        var root = doc.RootElement;

        var requestedModel = root.TryGetProperty("model", out var m)
            ? m.GetString() ?? DefaultModel
            : DefaultModel;

        var stream = root.TryGetProperty("stream", out var s) && s.ValueKind == JsonValueKind.True;

        var webSearch = ReadBool(root, "web_search")
                        || ReadHeaderBool(httpRequest, "X-Web-Search");

        var reasoningEffort = root.TryGetProperty("reasoning_effort", out var re)
            ? re.GetString()
            : httpRequest.Headers["X-Reasoning-Effort"];

        var thinking = !string.IsNullOrWhiteSpace(reasoningEffort)
                       || ReadBool(root, "ds_thinking");

        var search = webSearch || ReadBool(root, "ds_search");

        ResolveFeatureFlags(requestedModel, ref thinking, ref search);

        var resolvedModel = MapModel(requestedModel, config);

        var sessionId = root.TryGetProperty("session_id", out var sid)
            ? sid.GetString()
            : null;

        var webSession = sessions.ResolveWebSessionId(config, sessionId);

        var messages = ParseMessages(root);
        var refIds = ParseRefFileIds(root);

        return new CompletionRequest
        {
            RequestedModel = requestedModel,
            ResolvedModel = resolvedModel,
            Stream = stream,
            WebSearch = search,
            Thinking = thinking,
            SessionId = sessionId,
            WebChatSessionId = webSession,
            Messages = messages,
            RefFileIds = refIds
        };
    }

    public static string MapModel(string requestedModel, AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(requestedModel))
            return DefaultModel;

        var exact = config.ModelMappings.FirstOrDefault(
            x => x.RequestModel.Equals(requestedModel, StringComparison.OrdinalIgnoreCase));
        if (exact is not null && !string.IsNullOrWhiteSpace(exact.ActualModel))
            return exact.ActualModel;

        foreach (var entry in config.ModelMappings)
        {
            if (!entry.RequestModel.Contains('*')) continue;
            if (WildcardMatch(requestedModel, entry.RequestModel) &&
                !string.IsNullOrWhiteSpace(entry.ActualModel))
                return entry.ActualModel;
        }

        if (BuiltinMappings.TryGetValue(requestedModel, out var builtin))
            return builtin;

        return requestedModel;
    }

    public static IReadOnlyList<object> ListModels(AppConfig config)
    {
        EnsureDefaultMappings(config);
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ids = CollectDeepSeekModelIds(config);
        return ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(id => (object)BuildModelObject(id, created))
            .ToArray();
    }

    public static object? GetModel(string modelId, AppConfig config)
    {
        EnsureDefaultMappings(config);
        if (!CollectModelIds(config).Contains(modelId))
            return null;

        return BuildModelObject(modelId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static HashSet<string> CollectModelIds(AppConfig config) => CollectDeepSeekModelIds(config);

    private static HashSet<string> CollectDeepSeekModelIds(AppConfig config)
    {
        var ids = new HashSet<string>(DeepSeekModelIds, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in config.ModelMappings)
        {
            if (IsDeepSeekModelName(entry.RequestModel))
                ids.Add(entry.RequestModel);
            if (IsDeepSeekModelName(entry.ActualModel))
                ids.Add(entry.ActualModel);
        }

        return ids;
    }

    private static bool IsDeepSeekModelName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("DeepSeek", StringComparison.Ordinal));

    private static object BuildModelObject(string id, long created) => new
    {
        id,
        @object = "model",
        created,
        owned_by = "deepseek"
    };

    private static void ResolveFeatureFlags(string model, ref bool thinking, ref bool search)
    {
        var m = model.ToLowerInvariant();
        if (m.Contains("search")) search = true;
        if (m.Contains("r1") || m.Contains("think") || m.Contains("reasoner")) thinking = true;
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        var p = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(input, p, RegexOptions.IgnoreCase);
    }

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static bool ReadHeaderBool(System.Net.HttpListenerRequest req, string name)
    {
        var v = req.Headers[name];
        return v is "true" or "1" or "yes";
    }

    private static List<ChatMessage> ParseMessages(JsonElement root)
    {
        var messages = new List<ChatMessage>();
        if (!root.TryGetProperty("messages", out var msgs) || msgs.ValueKind != JsonValueKind.Array)
            return messages;

        foreach (var msg in msgs.EnumerateArray())
        {
            var role = msg.GetProperty("role").GetString() ?? "user";
            var chatMsg = new ChatMessage { Role = role };

            if (msg.TryGetProperty("content", out var c) && c.ValueKind != JsonValueKind.Null)
                chatMsg.Content = c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText();

            if (msg.TryGetProperty("tool_call_id", out var tid))
                chatMsg.ToolCallId = tid.GetString();

            if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                chatMsg.ToolCalls = new List<WebToolCall>();
                foreach (var tc in tcs.EnumerateArray())
                {
                    chatMsg.ToolCalls.Add(new WebToolCall
                    {
                        Id = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        Name = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                        Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                    });
                }
            }

            messages.Add(chatMsg);
        }

        return messages;
    }

    private static List<string> ParseRefFileIds(JsonElement root)
    {
        var ids = new List<string>();
        if (!root.TryGetProperty("ref_file_ids", out var refEl) || refEl.ValueKind != JsonValueKind.Array)
            return ids;

        foreach (var item in refEl.EnumerateArray())
        {
            var id = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id.Trim().Trim('"'));
        }

        return ids;
    }
}
