namespace DeepSeekBrowser.Models;

/// <summary>本地 DSD API 代理 Key（对齐 DSD API-main 的 ApiKey 结构）。</summary>
public sealed class LocalApiKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public long CreatedAt { get; set; }
    public long? LastUsedAt { get; set; }
    public int UsageCount { get; set; }
    public string? Description { get; set; }
}
