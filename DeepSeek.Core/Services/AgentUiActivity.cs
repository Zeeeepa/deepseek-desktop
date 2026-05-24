namespace DeepSeekBrowser.Services;

/// <summary>Agent 工作台 UI 活动行（Read / Grepped / Ran …）。</summary>
public readonly record struct AgentUiActivity(string Verb, string Target, string? Detail = null);
