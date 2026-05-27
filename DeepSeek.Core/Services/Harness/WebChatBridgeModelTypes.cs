namespace DeepSeekBrowser.Services.Harness;

/// <summary>映射 Agent 模型 id 到 DeepSeek 网页 Chat API 的 model_type。</summary>
public static class WebChatBridgeModelTypes
{
    public static string Resolve(string? model)
    {
        var m = (model ?? "").Trim().ToLowerInvariant();
        if (m.Contains("flash"))
            return "default";
        if (m.Contains("pro") || m.Contains("reasoner") || m.Contains("expert"))
            return "expert";
        return "default";
    }
}
