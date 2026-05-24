using System.Text;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 任务完成后写入复盘摘要（Harness 4.0 复盘思想 · DSD 实现：落盘到 runs，不自动改 L2/L3）。
/// </summary>
public static class HarnessPostMortemWriter
{
    public static void Write(
        string workspaceRoot,
        string runId,
        HarnessDomainMatch domain,
        string userPrompt,
        string answer,
        HarnessRunState state)
    {
        try
        {
            var dir = Path.Combine(workspaceRoot, ".deepseek", "runs", runId);
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, "postmortem.md");
            var sb = new StringBuilder();
            sb.AppendLine("# DSD Harness Post-Mortem");
            sb.AppendLine();
            sb.AppendLine("- 时间(UTC): " + DateTime.UtcNow.ToString("O"));
            sb.AppendLine("- 领域: " + domain.Name + " (" + domain.Id + ")");
            sb.AppendLine("- Phase: " + HarnessPhasePolicy.TraceLabel(state.Phase));
            sb.AppendLine("- Playbook: " + (state.PlaybookId ?? "—"));
            sb.AppendLine("- Skill: " + (state.SkillId ?? "—"));
            sb.AppendLine();
            sb.AppendLine("## 任务");
            sb.AppendLine(Trim(userPrompt, 500));
            sb.AppendLine();
            sb.AppendLine("## 结果摘要");
            sb.AppendLine(Trim(answer, 2000));
            sb.AppendLine();
            sb.AppendLine("## 下一步");
            sb.AppendLine(state.BlueprintFinalized
                ? "- 可按 Blueprint 进入 Execute"
                : "- 继续当前 Phase 或新开 Execute 任务");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

            var lastLink = Path.Combine(workspaceRoot, ".deepseek", "runs", "last-postmortem.txt");
            File.WriteAllText(lastLink, Path.GetRelativePath(workspaceRoot, path).Replace('\\', '/'), Encoding.UTF8);
        }
        catch
        {
            // 复盘写入失败不阻断
        }
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
