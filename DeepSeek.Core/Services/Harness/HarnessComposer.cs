using DeepSeekBrowser.Models;
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
        HarnessSkill? skill = null,
        HarnessRunIntent? intent = null,
        bool includeXmlTools = true)
    {
        var system = BuildSystemPrompt(
            request.Config, phase, workspaceRoot, workspaceSnapshot, mcpCatalog, profile, playbook, memory, skill, intent, includeXmlTools);
        if (!string.IsNullOrWhiteSpace(request.SubAgentRole))
        {
            var role = HarnessAgentRoleRegistry.Resolve(request.SubAgentRole);
            system = "## Role: " + role.DisplayName + "\n" + role.SystemPrompt + "\n\n---\n\n" + system;
        }

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
        AppConfig config,
        HarnessPhase phase,
        string workspaceRoot,
        string snapshot,
        string? mcpCatalog,
        HarnessStrategyProfile profile,
        HarnessPlaybook? playbook,
        HarnessMemoryContext? memory,
        HarnessSkill? skill,
        HarnessRunIntent? intent,
        bool includeXmlTools)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是 DeepSeek Edge Agent（DSD Harness · Phase 驱动）。");
        HarnessAgentDoctrine.Append(sb);
        sb.AppendLine("用户无需指定工具/MCP/Skill；按下方工具说明选用已验证存在的工具，禁止编造未列出的 MCP 名。");
        sb.AppendLine("工作区根：" + workspaceRoot + "；虚拟路径前缀：" + HarnessVirtualPathMapper.WorkspaceVirtual + "/、"
                        + HarnessVirtualPathMapper.OutputsVirtual + "/、"
                        + HarnessVirtualPathMapper.UploadsVirtual + "/（只读）、"
                        + HarnessVirtualPathMapper.SkillsVirtual + "/（只读）");
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

        var minimalIntent = HarnessPromptBudget.UseMinimalIntentSection(config);
        if (intent is not null)
        {
            sb.AppendLine();
            sb.Append(intent.BuildPromptSection(
                includeXmlTools,
                HarnessPromptBudget.IntentPlannedToolLimit(config),
                minimalIntent));
        }

        var skipToolCatalog = HarnessPromptBudget.ShouldSkipToolCatalog(config, intent, includeXmlTools);
        if (includeXmlTools && !skipToolCatalog && phase is HarnessPhase.Orient or HarnessPhase.Explore or HarnessPhase.Execute)
        {
            sb.AppendLine();
            sb.AppendLine(HarnessToolRegistry.BuildBuiltinToolsSectionCompact());
            var trimmedMcp = HarnessPromptBudget.TrimMcpCatalog(
                mcpCatalog, Math.Clamp(config.AgentMcpCatalogMaxLines, 8, 120));
            sb.Append(HarnessToolRegistry.BuildMcpSection(trimmedMcp));
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
            AppendSkillSection(sb, skill, config);

        return sb.ToString().TrimEnd();
    }

    private static void AppendSkillSection(System.Text.StringBuilder sb, HarnessSkill skill, AppConfig config)
    {
        sb.AppendLine();
        sb.AppendLine("【Skill · " + skill.Name + " · " + skill.Source + "】");
        if (!string.IsNullOrWhiteSpace(skill.Description))
            sb.AppendLine(skill.Description.Trim());

        var maxChars = HarnessPromptBudget.DefaultSkillMaxChars(config);
        var body = skill.Body.Trim();
        if (body.Length > maxChars)
            body = body[..maxChars] + "\n…(Skill 正文已截断，完整内容见 " + skill.FilePath + ")";
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
        sb.AppendLine("【Phase: Explore · 探索】只读调研，禁止 write/shell；先 list/glob 再 grep/read。");
        sb.AppendLine("寒暄或无具体任务时直接简短回复，勿编造调研结论。");
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
        sb.AppendLine("【Phase: Execute · 执行】读写/shell 完成任务；delegate_agent / parallel_explore 按需使用。");
        sb.AppendLine(
            "首轮：用【任务分析】1～2 句说明计划，并在同一轮内立即发出 <tool_calling> 工具（可先 list_dir 再 write_file）；禁止只写分析不调用工具。");
        sb.AppendLine("多步任务先 UpdatePlan，再逐项执行；修改已有文件优先 edit（old_string/new_string），新建文件用 write_file。");
        sb.AppendLine("工具调用必须使用 <tool_calling><name>…</name><arguments>{…}</arguments></tool_calling>，勿写 Call: 纯文本。");
        sb.AppendLine("禁止在正文或 ```json``` 中输出 {\"name\":\"…\",\"arguments\":…} 形式的工具 JSON；工具只通过 <tool_calling> 发出。");
    }

    public static ChatMessage BuildIncompleteToolCallRetryMessage() => new()
    {
        Role = "user",
        Content =
            "你提到了工具名但未给出可执行的 <tool_calling> 块（禁止仅用 Call: 文本）。\n" +
            "请立即输出完整格式，例如：\n" +
            "<tool_calling>\n<name>write_file</name>\n" +
            "<arguments>{\"file_path\":\"index.html\",\"content\":\"...\"}</arguments>\n</tool_calling>"
    };

    public static ChatMessage BuildExecuteProseOnlyRetryMessage() => new()
    {
        Role = "user",
        Content =
            "你已输出【任务分析】/计划，但尚未调用任何工具（勿只写空 ```json``` 代码块）。\n" +
            "请在本轮直接发出可执行工具，例如：\n" +
            "<tool_calling>\n<name>list_dir</name>\n<arguments>{\"path\":\".\"}</arguments>\n</tool_calling>\n" +
            "然后再 write_file 写入完整可运行文件。"
    };

    public static ChatMessage BuildEmptyExecuteRetryMessage(string userPrompt) => new()
    {
        Role = "user",
        Content =
            "上一条为空。请先写【任务分析】1～2 句，再对「" + (userPrompt ?? "").Trim() +
            "」调用 write_file / list_dir / run_shell 或给出可见正文，禁止再次空回复。"
    };

    private static void AppendVerifyInstructions(System.Text.StringBuilder sb)
    {
        sb.AppendLine("【Phase: Verify · 验证】");
        sb.AppendLine("目标：基于 Harness 已执行的验收命令输出，给出通过/失败结论与摘要。");
        sb.AppendLine("禁止：调用任何工具。");
    }
}
