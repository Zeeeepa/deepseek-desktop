namespace DeepSeekBrowser.Models;

/// <summary>Chat2API 模型映射：请求模型名 → 实际 DeepSeek 网页模型。</summary>
public sealed class ModelMappingEntry
{
    public string RequestModel { get; set; } = "";
    public string ActualModel { get; set; } = "";
}
