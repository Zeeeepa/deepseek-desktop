namespace DeepSeekBrowser.Services.Harness;

public enum HarnessAgentFsmState
{
    Plan,
    ExecuteTool,
    Reflect,
    WaitForApproval,
    Complete,
    Failed,
    Idle
}

public sealed class HarnessAgentStateMachine
{
    public HarnessAgentFsmState State { get; private set; } = HarnessAgentFsmState.Plan;
    public int ReflectCount { get; private set; }
    public int SameFileLspErrorStreak { get; private set; }
    public string? LastReflectFile { get; private set; }

    public void Transition(HarnessAgentFsmState next) => State = next;

    public string? RecordReflect(string? editedFile, int diagnosticErrors, string? shellSummary)
    {
        ReflectCount++;
        if (!string.IsNullOrWhiteSpace(editedFile))
        {
            if (string.Equals(LastReflectFile, editedFile, StringComparison.OrdinalIgnoreCase)
                && diagnosticErrors > 0)
                SameFileLspErrorStreak++;
            else if (diagnosticErrors > 0)
                SameFileLspErrorStreak = 1;
            else
                SameFileLspErrorStreak = 0;
            LastReflectFile = editedFile;
        }

        if (SameFileLspErrorStreak >= 3)
        {
            State = HarnessAgentFsmState.Failed;
            return "同一文件 LSP 错误连续 3 轮未减少，请人工接管。";
        }

        if (diagnosticErrors > 0)
        {
            State = HarnessAgentFsmState.Reflect;
            return "Reflect: 仍有 " + diagnosticErrors + " 个 Error 级诊断，请修复。\n"
                   + (shellSummary ?? "");
        }

        State = HarnessAgentFsmState.ExecuteTool;
        return null;
    }
}
