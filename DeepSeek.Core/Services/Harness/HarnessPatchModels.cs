namespace DeepSeekBrowser.Services.Harness;

public enum HarnessPatchApplyMode
{
    Direct,
    Preview
}

public sealed class HarnessPatchRequest
{
    public required string FilePath { get; init; }
    public required string OldString { get; init; }
    public required string NewString { get; init; }
    public bool ReplaceAll { get; init; }
}

public sealed class HarnessPatchResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int MatchCount { get; init; }
    public string? ClosestMatchHint { get; init; }
    public string OriginalContent { get; init; } = "";
    public string PatchedContent { get; init; } = "";
    public string UnifiedDiff { get; init; } = "";
}

public readonly record struct AgentPendingPatch(
    string PatchId,
    string Path,
    string Language,
    string OriginalContent,
    string PatchedContent,
    string UnifiedDiff,
    bool AppliedToDisk);

public readonly record struct AgentPatchResolution(
    string PatchId,
    bool Accepted);
