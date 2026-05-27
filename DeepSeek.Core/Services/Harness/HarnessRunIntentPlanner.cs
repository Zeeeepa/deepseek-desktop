using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.Harness.Interop;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// Run 前意图分析：理解用户需求 → 匹配 Skill → 规划工具并校验是否存在。
/// </summary>
public static class HarnessRunIntentPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static bool ShouldPlan(HarnessRunRequest request, HarnessRunState? state) =>
        request.Config.AgentAutoIntentRouting
        && string.IsNullOrWhiteSpace(request.SubAgentRole)
        && string.IsNullOrWhiteSpace(request.ResumeGraphThreadId)
        && (state?.Messages is null || state.Messages.Count == 0)
        && !IsCasualOnly(request.Prompt);

    public static async Task<HarnessRunIntent?> PlanAsync(
        HarnessRunRequest request,
        IAgentWebChat chat,
        string? mcpCatalog,
        string workspace,
        CancellationToken ct)
    {
        if (!request.Config.AgentAutoIntentRouting)
            return null;

        var inventory = HarnessToolInventory.Build(request.Config, mcpCatalog);
        var skillCandidates = RankSkills(request.Prompt, workspace, request.Config.AgentSkillExtraRoots);

        HarnessRunIntent intent;
        if (ShouldUseLlmPlanner(request))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                intent = await PlanWithLlmAsync(
                    chat, request, inventory, skillCandidates, timeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                intent = PlanHeuristic(request, inventory, skillCandidates);
                intent = CopyIntent(intent,
                    executionNotes: (intent.ExecutionNotes ?? "") + " LLM 规划回退：" + ex.Message);
            }
        }
        else
            intent = PlanHeuristic(request, inventory, skillCandidates);

        if (string.IsNullOrWhiteSpace(request.SkillId)
            && !string.IsNullOrWhiteSpace(intent.SelectedSkillId))
            intent = CopyIntent(intent, autoSelectedSkill: true);

        return intent;
    }

    private static HarnessRunIntent CopyIntent(
        HarnessRunIntent src,
        string? analysis = null,
        string? executionNotes = null,
        bool? autoSelectedSkill = null) =>
        new()
        {
            Analysis = analysis ?? src.Analysis,
            SelectedSkillId = src.SelectedSkillId,
            SelectedSkillName = src.SelectedSkillName,
            PlannedTools = src.PlannedTools,
            ExecutionNotes = executionNotes ?? src.ExecutionNotes,
            UsedLlm = src.UsedLlm,
            AutoSelectedSkill = autoSelectedSkill ?? src.AutoSelectedSkill
        };

    public static bool ShouldUseLlmPlanner(HarnessRunRequest request) =>
        request.Config.AgentIntentUseLlmPlanner
        && !IsCasualOnly(request.Prompt);

    private static async Task<HarnessRunIntent> PlanWithLlmAsync(
        IAgentWebChat chat,
        HarnessRunRequest request,
        HarnessToolInventory inventory,
        IReadOnlyList<ScoredSkill> skillCandidates,
        CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(request.Config.Model)
            ? AgentModeHelper.AgentModel
            : request.Config.Model;

        var skillBlock = BuildSkillCandidateBlock(skillCandidates);
        var plannerPrompt = BuildPlannerUserPrompt(request.Prompt, skillBlock, inventory);

        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content =
                    "你是 DeepSeek Harness 的「运行前意图分析器」。只输出一个 JSON 对象，不要 markdown 代码围栏，不要调用工具。\n" +
                    "字段：analysis(string), skill_id(string|null), tools(array of {name,purpose}), notes(string|null).\n" +
                    "skill_id 必须从「候选 Skill」列表中选择，无合适则 null。\n" +
                    "tools[].name 必须来自「可用工具」列表或为其常见别名（read/write/bash/delegate_agent 等）。"
            },
            new() { Role = "user", Content = plannerPrompt }
        };

        var result = await chat.CompleteAsync(
            messages,
            model,
            thinking: false,
            search: false,
            request.RefFileIds,
            allowToolCalls: false,
            ct,
            AccountCredentials.ResolveWebUserTokenForRoute(null, request.Config, "deepseek"),
            request.WebChatSessionId);

        var parsed = TryParseLlmJson(result.Content);
        if (parsed is null)
            return CopyIntent(PlanHeuristic(request, inventory, skillCandidates),
                executionNotes: "LLM 返回无法解析为 JSON，已使用启发式规划。");

        return BuildIntentFromLlm(parsed, inventory, skillCandidates, request);
    }

    private static HarnessRunIntent PlanHeuristic(
        HarnessRunRequest request,
        HarnessToolInventory inventory,
        IReadOnlyList<ScoredSkill> skillCandidates)
    {
        var prompt = request.Prompt ?? "";
        var tools = InferToolsHeuristic(prompt, request.Config, inventory);
        var top = skillCandidates.FirstOrDefault();
        var pickSkill = top is { Score: >= 4 } && (skillCandidates.Count < 2 || top.Score >= skillCandidates[1].Score + 2)
            ? top
            : null;

        var analysis = ClassifyTask(prompt);
        return new HarnessRunIntent
        {
            Analysis = analysis,
            SelectedSkillId = pickSkill?.Id,
            SelectedSkillName = pickSkill?.Name,
            PlannedTools = tools,
            ExecutionNotes = BuildHeuristicNotes(prompt, request.Config),
            UsedLlm = false
        };
    }

    private static HarnessRunIntent BuildIntentFromLlm(
        LlmIntentDto dto,
        HarnessToolInventory inventory,
        IReadOnlyList<ScoredSkill> skillCandidates,
        HarnessRunRequest request)
    {
        var skillId = dto.SkillId?.Trim();
        ScoredSkill? skill = null;
        if (!string.IsNullOrWhiteSpace(skillId))
            skill = skillCandidates.FirstOrDefault(s =>
                s.Id.Equals(skillId, StringComparison.OrdinalIgnoreCase));

        var planned = new List<HarnessPlannedTool>();
        foreach (var t in dto.Tools ?? [])
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            var resolved = inventory.ResolveAvailableName(t.Name);
            var available = resolved is not null;
            planned.Add(new HarnessPlannedTool
            {
                Name = t.Name,
                Purpose = t.Purpose ?? "",
                IsAvailable = available,
                Fallback = available ? null : SuggestFallback(t.Name, inventory, request.Config)
            });
        }

        if (planned.Count == 0)
            planned.AddRange(InferToolsHeuristic(request.Prompt, request.Config, inventory));

        return new HarnessRunIntent
        {
            Analysis = dto.Analysis?.Trim() ?? ClassifyTask(request.Prompt),
            SelectedSkillId = skill?.Id,
            SelectedSkillName = skill?.Name,
            PlannedTools = planned,
            ExecutionNotes = dto.Notes?.Trim(),
            UsedLlm = true
        };
    }

    private static IReadOnlyList<HarnessPlannedTool> InferToolsHeuristic(
        string prompt,
        AppConfig config,
        HarnessToolInventory inventory)
    {
        var list = new List<HarnessPlannedTool>();
        void Add(string name, string purpose)
        {
            var resolved = inventory.ResolveAvailableName(name) ?? name;
            list.Add(new HarnessPlannedTool
            {
                Name = name,
                Purpose = purpose,
                IsAvailable = inventory.IsAvailable(name),
                Fallback = inventory.IsAvailable(name) ? null : SuggestFallback(name, inventory, config)
            });
        }

        var p = prompt.ToLowerInvariant();
        if (p.Contains("搜索") || p.Contains("search") || p.Contains("查一下"))
            Add("WebSearch", "检索外部信息");
        if (p.Contains("测试") || p.Contains("test") || p.Contains("build") || p.Contains("编译"))
            Add("run_shell", "运行验证命令");
        if (p.Contains("代码") || p.Contains("文件") || p.Contains("bug") || p.Contains("refactor")
            || p.Contains("implement") || p.Contains("修复")
            || p.Contains("创建") || p.Contains("游戏") || p.Contains("小游戏") || p.Contains("贪吃蛇"))
        {
            Add("write_file", "创建或更新实现文件");
            Add("grep", "定位相关符号与引用");
            Add("read_file", "阅读实现与上下文");
        }

        if (NeedsBroadExploration(p) && config.EnableParallelExplore)
            Add("parallel_explore", "并行只读调研后汇总");
        else if (NeedsBroadExploration(p))
        {
            Add("list_dir", "了解目录结构");
            Add("glob", "发现相关文件");
        }

        if (config.EnableSubAgents && (p.Contains("审查") || p.Contains("review") || p.Length > 120))
            Add("delegate_agent", "委派专长子 Agent");

        if (list.Count == 0 && !IsCasualOnly(prompt))
        {
            Add("list_dir", "初步了解工作区");
            Add("read_file", "按需阅读关键文件");
        }

        return list;
    }

    private static bool NeedsBroadExploration(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        return p.Contains("架构") || p.Contains("梳理") || p.Contains("explore") || p.Contains("调研")
               || p.Contains("overview") || p.Contains("where is") || p.Contains("在哪");
    }

    private static string? SuggestFallback(string name, HarnessToolInventory inventory, AppConfig config)
    {
        var n = name.ToLowerInvariant();
        if (n is "parallel_explore" && !config.EnableParallelExplore)
            return "list_dir + grep + read_file";
        if (n is "delegate_agent" && !config.EnableSubAgents)
            return null;
        if (inventory.IsAvailable("read_file")) return "read_file";
        if (inventory.IsAvailable("grep")) return "grep";
        return null;
    }

    private static string ClassifyTask(string prompt)
    {
        if (IsCasualOnly(prompt))
            return "寒暄或简单问答，通常无需工具。";
        if (NeedsBroadExploration(prompt))
            return "需要对工作区进行调研与信息汇总。";
        var p = prompt.ToLowerInvariant();
        if (p.Contains("写") || p.Contains("改") || p.Contains("fix") || p.Contains("add")
            || p.Contains("创建") || p.Contains("游戏") || p.Contains("贪吃蛇"))
            return "需要在 workspace 内创建或实现文件/功能。";
        return "通用任务：先简要确认范围，再调用工具执行。";
    }

    private static string BuildHeuristicNotes(string prompt, AppConfig config)
    {
        if (IsCasualOnly(prompt))
            return "直接回复用户即可。";
        if (NeedsBroadExploration(prompt) && config.EnableParallelExplore)
            return "优先 parallel_explore 或只读工具链，避免未调研就修改文件。";
        return "按 PlannedTools 顺序执行，每步工具调用应有明确目的。";
    }

    public static bool IsCasualOnly(string? prompt)
    {
        var t = (prompt ?? "").Trim();
        if (t.Length == 0) return true;
        if (t.Length > 80) return false;
        var lower = t.ToLowerInvariant();
        string[] casual =
        [
            "hi", "hello", "hey", "thanks", "thank you", "你好", "您好", "谢谢", "在吗", "嗨"
        ];
        return casual.Any(c => lower == c || lower.StartsWith(c + " ") || lower.StartsWith(c + "，"));
    }

    private static IReadOnlyList<ScoredSkill> RankSkills(
        string prompt,
        string workspace,
        IReadOnlyList<string>? extraRoots)
    {
        var skills = HarnessSkillRegistry.List(workspace, extraRoots);
        if (skills.Count == 0) return Array.Empty<ScoredSkill>();

        var tokens = Tokenize(prompt);
        var scored = new List<ScoredSkill>();
        foreach (var s in skills)
        {
            var score = 0;
            foreach (var tok in tokens)
            {
                if (tok.Length < 3) continue;
                if (s.Id.Contains(tok, StringComparison.OrdinalIgnoreCase)) score += 3;
                if (s.Name.Contains(tok, StringComparison.OrdinalIgnoreCase)) score += 2;
                if (s.Description?.Contains(tok, StringComparison.OrdinalIgnoreCase) == true) score += 2;
            }

            if (score > 0)
                scored.Add(new ScoredSkill(s.Id, s.Name, score));
        }

        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(6).ToList();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        foreach (Match m in Regex.Matches(text, @"[\p{L}\p{N}_]{3,}", RegexOptions.None))
            yield return m.Value;
    }

    private static string BuildSkillCandidateBlock(IReadOnlyList<ScoredSkill> candidates)
    {
        if (candidates.Count == 0)
            return "（无已安装 Skill）";

        var lines = candidates.Take(6).Select(c => $"- {c.Id}: {c.Name}");
        return string.Join('\n', lines);
    }

    private static string BuildPlannerUserPrompt(
        string userPrompt,
        string skillBlock,
        HarnessToolInventory inventory) =>
        "用户任务：\n" + userPrompt + "\n\n候选 Skill（skill_id 只能从中选）：\n" + skillBlock +
        "\n\n" + inventory.BuildCompactNameList() + "\n\n请输出 JSON。";

    private static LlmIntentDto? TryParseLlmJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var json = text.Trim();
        var fence = Regex.Match(json, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (fence.Success)
            json = fence.Groups[1].Value;

        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        json = json[start..(end + 1)];

        try
        {
            return JsonSerializer.Deserialize<LlmIntentDto>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private sealed class LlmIntentDto
    {
        public string? Analysis { get; set; }
        public string? SkillId { get; set; }
        public List<LlmToolDto>? Tools { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class LlmToolDto
    {
        public string? Name { get; set; }
        public string? Purpose { get; set; }
    }

    private sealed record ScoredSkill(string Id, string Name, int Score);
}
