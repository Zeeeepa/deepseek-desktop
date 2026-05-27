namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// Agent 行为准则（提炼自 DeepSeek-TUI / deepcode-cli / MetaGPT 等开源 Agent 实践）。
/// 由模型在对话内自行分析任务，不依赖外部预分类。
/// </summary>
public static class HarnessAgentDoctrine
{
    public static void Append(System.Text.StringBuilder sb)
    {
        sb.AppendLine("## Agent 工作准则");
        sb.AppendLine("1. **任务分析在对话内完成**：先判断类型（实现/调研/问答/运维）与验收标准，再选工具；简单问答直接简短回复。");
        sb.AppendLine("2. **并行工具**：同一轮内独立的只读操作（list_dir/glob/grep/read_file）应一次发出多个 tool call，不要串行等待。");
        sb.AppendLine("3. **多步任务**：估计 ≥3 步时先用 UpdatePlan 写 Markdown 任务清单（[ ]/[>]/[x]），每步开始/结束更新计划。");
        sb.AppendLine("4. **先预览再动手**：陌生仓库先 list_dir + 读 README/入口文件，再 grep/read；避免无目标全库搜索。");
        sb.AppendLine("5. **验证再声称完成**：改代码后 run_shell 测试或 read_file 确认；失败则说明原因，不编造结果。");
        sb.AppendLine("6. **失败不盲重试**：相同工具+参数连续失败时换思路；勿重复已阻止的调用。");
        sb.AppendLine("7. **子任务委派**：大范围调研可用 delegate_agent / parallel_explore，你负责综合结论。");
        sb.AppendLine("8. **展示代码**：在回复中贴出完整源码时，必须用 Markdown ```lang 代码围栏包裹（如 ```html），勿只写「见上文」；实现类任务仍优先 write_file 落盘。");
        sb.AppendLine();
    }
}
