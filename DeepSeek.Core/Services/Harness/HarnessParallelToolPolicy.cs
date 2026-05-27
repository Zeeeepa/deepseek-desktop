namespace DeepSeekBrowser.Services.Harness;

/// <summary>哪些工具可在单轮内并行执行（对齐 DeepSeek-TUI 原生 parallel tool_calls）。</summary>
public static class HarnessParallelToolPolicy
{
    public static bool CanRunInParallel(string toolName, HarnessPhase phase)
    {
        var normalized = BuiltinToolExecutor.NormalizeName(toolName);
        if (phase == HarnessPhase.Execute)
        {
            return normalized is "read_file" or "grep" or "glob" or "list_dir" or "image_analyze";
        }

        if (HarnessPhasePolicy.IsReadonlyPhase(phase))
            return IsReadonlyBuiltin(normalized);

        return false;
    }

    private static bool IsReadonlyBuiltin(string normalized) =>
        normalized is "read_file" or "grep" or "glob" or "list_dir" or "image_analyze";
}
