using DeepSeekBrowser.Services.Harness.Interop;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// L3 Composer：按 Phase 装配系统提示、阶段指令与工具说明（DSD Harness 原创，非外部 skill 模板）。
/// </summary>
public static class HarnessComposer
{
    public static IReadOnlyList<ChatMessage> BuildInitialMessages(
        HarnessRunRequest request,
        string workspaceRoot,
        string? mcpCatalog,
        string workspaceSnapshot,
        HarnessPhase phase,
        HarnessStrategyProfile profile,
        HarnessPlaybook? playbook = null,
        HarnessMemoryContext? memory = null,
        HarnessSkill? skill = null)
    {
        var system = BuildSystemPrompt(phase, workspaceRoot, workspaceSnapshot, mcpCatalog, profile, playbook, memory, skill);
        return
        [
            new ChatMessage { Role = "system", Content = system },
            new ChatMessage { Role = "user", Content = request.Prompt }
        ];
    }

    public static ChatMessage BuildBlueprintFinalizeUserMessage() => new()
    {
        Role = "user",
        Content =
            "Explore 阶段结束。请**不要调用任何工具**，进入 Blueprint 阶段，直接输出结构化方案，使用以下 Markdown：\n\n" +
            "## 目标\n## 现状摘要\n## 建议步骤（编号）\n## 风险与依赖\n## 验收标准\n\n" +
            "内容应基于上文工具 Observation，不要编造未验证的事实。"
    };

    public static ChatMessage BuildOrientToExploreTransition() => new()
    {
        Role = "user",
        Content =
            "Orient 完成。请进入 Explore 阶段：用只读工具继续调研，为 Blueprint 收集证据。"
    };

    public static ChatMessage BuildVerifyUserMessage(string verifyOutput, bool passed) => new()
    {
        Role = "user",
        Content =
            "Execute 阶段已完成。Harness 已运行 Verify 验收命令，结果如下：\n\n" +
            "```\n" + verifyOutput + "\n```\n\n" +
            (passed
                ? "Verify 通过。请**不要调用工具**，用简短 Markdown 总结：完成了什么、Verify 结果、后续建议。"
                : "Verify **未通过**。请**不要调用工具**，说明失败原因、可能修复方向与是否需人工介入。")
    };

    private static string BuildSystemPrompt(
        HarnessPhase phase,
        string workspaceRoot,
        string snapshot,
        string? mcpCatalog,
        HarnessStrategyProfile profile,
        HarnessPlaybook? playbook,
        HarnessMemoryContext? memory,
        HarnessSkill? skill)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是 DeepSeek Edge Agent（DSD Harness · Phase 驱动）。");
        sb.AppendLine("Harness 仅按 Phase 限制工具类型（例如 Blueprint 阶段禁止工具）；是否调用工具、是否需要长篇推理，完全由你根据用户意图自行判断，客户端不会预判消息类型。");
        sb.AppendLine("寒暄、闲聊、简单问答：直接简短友好回复即可，通常无需调用工具，也无需冗长思考过程。");
        sb.AppendLine("用户给出明确任务且需要读文件、改代码或执行命令时，再调用相应工具并充分推理。");
        sb.AppendLine("工作区根目录（物理）：" + workspaceRoot);
        sb.AppendLine("虚拟路径（工具参数请优先使用）：");
        sb.AppendLine("  " + HarnessVirtualPathMapper.WorkspaceVirtual + "/ — 读写工作区");
        sb.AppendLine("  " + HarnessVirtualPathMapper.OutputsVirtual + "/ — 任务输出");
        sb.AppendLine("  " + HarnessVirtualPathMapper.UploadsVirtual + "/ — 只读（用户上传）");
        sb.AppendLine("  " + HarnessVirtualPathMapper.SkillsVirtual + "/ — 只读（~/.deepseek/skills）");
        sb.AppendLine();
        sb.AppendLine(snapshot);
        sb.AppendLine();

        if (memory is not null && !memory.IsEmpty)
        {
            sb.AppendLine(memory.BuildPromptSection());
            sb.AppendLine();
        }

        switch (phase)
        {
            case HarnessPhase.Orient:
                AppendOrientInstructions(sb);
                break;
            case HarnessPhase.Explore:
                AppendExploreInstructions(sb);
                break;
            case HarnessPhase.Blueprint:
                AppendBlueprintInstructions(sb);
                break;
            case HarnessPhase.Execute:
                AppendExecuteInstructions(sb);
                break;
            case HarnessPhase.Verify:
                AppendVerifyInstructions(sb);
                break;
        }

        if (phase is HarnessPhase.Orient or HarnessPhase.Explore or HarnessPhase.Execute)
        {
            sb.AppendLine();
            sb.AppendLine(HarnessToolRegistry.BuildBuiltinToolsSection());
            sb.Append(HarnessToolRegistry.BuildMcpSection(mcpCatalog));
        }

        if (profile.Workflow == HarnessWorkflow.Blueprint && phase == HarnessPhase.Explore)
        {
            sb.AppendLine();
            sb.AppendLine(
                "工作流：Orient（可选）→ Explore（只读调研）→ Blueprint（无工具输出方案）。" +
                "收到「Explore 阶段结束」后进入 Blueprint。");
        }

        if (playbook is not null)
            AppendPlaybookSection(sb, playbook);

        if (skill is not null)
            AppendSkillSection(sb, skill);

        return sb.ToString().TrimEnd();
    }

    private static void AppendSkillSection(System.Text.StringBuilder sb, HarnessSkill skill)
    {
        sb.AppendLine();
        sb.AppendLine("【Market Skill · " + skill.Name + " · " + skill.Source + "】");
        if (!string.IsNullOrWhiteSpace(skill.Description))
            sb.AppendLine(skill.Description.Trim());

        var body = skill.Body.Trim();
        if (body.Length > 12_000)
            body = body[..12_000] + "\n…(Skill 正文已截断，完整内容见 " + skill.FilePath + ")";
        sb.AppendLine();
        sb.AppendLine(body);
    }

    private static void AppendPlaybookSection(System.Text.StringBuilder sb, HarnessPlaybook playbook)
    {
        sb.AppendLine();
        sb.AppendLine("【Playbook · " + playbook.Name + "】");
        if (!string.IsNullOrWhiteSpace(playbook.Description))
            sb.AppendLine(playbook.Description.Trim());
        if (playbook.Steps.Count > 0)
        {
            sb.AppendLine("步骤：");
            for (var i = 0; i < playbook.Steps.Count; i++)
                sb.AppendLine($"{i + 1}. {playbook.Steps[i]}");
        }

        if (!string.IsNullOrWhiteSpace(playbook.SystemAppend))
        {
            sb.AppendLine();
            sb.AppendLine(playbook.SystemAppend.Trim());
        }
    }

    private static void AppendOrientInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Orient · 定向】");
        sb.AppendLine("目标：澄清用户意图、列出关键假设、指出需要 Explore 的路径。");
        sb.AppendLine("可用工具：只读（list_dir / glob / grep / read_file、MCP 只读）。");
        sb.AppendLine("禁止：write_file、run_shell 及任何修改状态的操作。");
        sb.AppendLine("产出：简短定向摘要（目标 / 假设 / 下一步 Explore 焦点）。");
    }

    private static void AppendExploreInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Explore · 探索】");
        sb.AppendLine("目标：用只读工具建立事实链，为 Blueprint 提供可引用证据。");
        sb.AppendLine("禁止：write_file、run_shell 及任何会修改状态的操作。");
        sb.AppendLine("策略：先 list_dir/glob 定位，再 grep/read_file 深入；Observation 要可追溯到文件路径。");
        sb.AppendLine("若用户仅为寒暄、闲聊或无具体任务（如 hello），请直接简短友好回复，勿输出 Blueprint 结构或编造调研结论；是否调用只读工具由你判断，通常不必。");
    }

    private static void AppendBlueprintInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Blueprint · 蓝图】");
        sb.AppendLine("目标：基于 Explore 的 Observation，输出可执行方案。");
        sb.AppendLine("禁止：调用任何工具。");
        sb.AppendLine("结构：目标 / 现状摘要 / 建议步骤 / 风险与依赖 / 验收标准。");
    }

    private static void AppendExecuteInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Execute · 执行】");
        sb.AppendLine("目标：通过工具完成任务（读、写、搜索、必要时 shell）。");
        sb.AppendLine("策略：先收集事实再动手；每次工具调用应有明确目的；最终给出完整结果。");
        sb.AppendLine("是否调用工具由你根据用户意图判断：无具体任务时直接回复，有任务再调用工具。");
    }

    private static void AppendVerifyInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Verify · 验证】");
        sb.AppendLine("目标：基于 Harness 已执行的验收命令输出，给出通过/失败结论与摘要。");
        sb.AppendLine("禁止：调用任何工具。");
    }
}
