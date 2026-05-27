using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessToolExecuteResult
{
    public string Output { get; init; } = "";
    public IReadOnlyList<ChatMessage> FollowUpMessages { get; init; } = Array.Empty<ChatMessage>();
    public AgentPendingPatch? PendingPatch { get; init; }
    public AgentFilePreview? FilePreview { get; init; }

    public static HarnessToolExecuteResult FromOutput(string output) => new() { Output = output };

    public static HarnessToolExecuteResult FromEdit(HarnessEditResult edit) =>
        new()
        {
            Output = edit.ToToolOutput(),
            PendingPatch = edit.Pending,
            FilePreview = edit.Ok && edit.VirtualPath is not null && edit.Patch is not null
                ? new AgentFilePreview(
                    edit.VirtualPath,
                    HarnessFilePreview.LanguageFromExtension(edit.VirtualPath),
                    edit.Patch.PatchedContent,
                    Complete: edit.Pending is null)
                : null
        };

    public static HarnessToolExecuteResult WithFollowUp(string output, IEnumerable<ChatMessage> followUps) =>
        new()
        {
            Output = output,
            FollowUpMessages = followUps.ToList()
        };
}
