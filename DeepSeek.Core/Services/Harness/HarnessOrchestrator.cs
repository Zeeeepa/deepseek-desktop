using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.Harness.Interop;
using DeepSeekBrowser.Services.Harness.Memory;
using DeepSeekBrowser.Services.Harness.Observability;
using DeepSeekBrowser.Services.Harness.Sandbox;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessOrchestrator
{
    private static readonly JsonSerializerOptions StateJson = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IAgentWebChat _chat;
    private readonly McpHub _mcp;
    private readonly HarnessTrace _trace = new();
    private readonly IUserQuestionHandler? _userQuestions;
    private readonly string? _workspaceForHistory;

    public HarnessOrchestrator(
        IAgentWebChat chat,
        McpHub mcp,
        PermissionGate permission,
        IUserQuestionHandler? userQuestions = null,
        string? workspaceForHistory = null,
        HarnessSubAgentService? subAgents = null)
    {
        _chat = chat;
        _mcp = mcp;
        _userQuestions = userQuestions;
        _workspaceForHistory = workspaceForHistory;
        _tools = new HarnessToolExecutor(mcp, permission, _trace, userQuestions, workspaceForHistory, subAgents);
        _kernel = new HarnessLoopKernel(_chat, _mcp, _tools);
    }

    private readonly HarnessToolExecutor _tools;
    private readonly HarnessLoopKernel _kernel;

    public async Task<HarnessRunResult> RunAsync(
        HarnessRunRequest request,
        HarnessRunCallbacks callbacks,
        CancellationToken ct)
    {
        var config = request.Config;
        var workspace = AgentWorkspace.ResolveRoot(config);
        _tools.SetAgentSessionId(request.AgentSessionId);
        HarnessPlaybook? playbook = null;
        if (!string.IsNullOrWhiteSpace(request.PlaybookId))
            HarnessPlaybookRegistry.TryGet(request.PlaybookId, workspace, out playbook);

        HarnessSkill? skill = null;
        if (!string.IsNullOrWhiteSpace(request.SkillId))
            HarnessSkillRegistry.TryGet(request.SkillId, workspace, out skill);

        var strategy = request.Strategy;
        if (playbook?.Strategy is { Length: > 0 } pbStrategy)
            strategy = pbStrategy;

        var profile = HarnessStrategyResolver.Resolve(strategy);
        var maxTurns = Math.Clamp(request.MaxStepsOverride ?? config.MaxAgentSteps, 1, 50);
        var researchCap = HarnessPhasePolicy.ResearchCap(profile.Workflow, maxTurns);

        var state = DeserializeState(request.ExistingHarnessState, profile) ?? new HarnessRunState
        {
            Phase = profile.InitialPhase
        };

        await using var prep = await _kernel.PrepareRunContextAsync(request, callbacks, state, ct);
        var sandboxCoord = prep.SandboxCoordinator;

        callbacks.OnLog?.Invoke("正在准备对话上下文…");
        AgentDebugLogger.Current?.Write("HARNESS", "prep: memory + MCP catalog");

        var memory = prep.Memory;
        var domain = new HarnessDomainMatch { Id = memory.DomainId, Name = memory.DomainName };
        if (playbook is not null)
            state.PlaybookId = playbook.Id;
        else if (!string.IsNullOrWhiteSpace(request.PlaybookId))
            state.PlaybookId = request.PlaybookId;
        if (skill is not null)
            state.SkillId = skill.Id;
        else if (!string.IsNullOrWhiteSpace(request.SkillId))
            state.SkillId = request.SkillId;
        state.DomainId = memory.DomainId;
        state.RunId ??= "run-" + Guid.NewGuid().ToString("N")[..12];

        NotifyPhase(state.Phase, callbacks);
        var webSessionId = request.WebChatSessionId ?? state.WebChatSessionId;
        var modelForRoute = string.IsNullOrWhiteSpace(config.Model) ? AgentModeHelper.AgentModel : config.Model;
        var route = ApiRouteResolver.Resolve(config, _chat, config.AgentDefaultProviderId, modelForRoute);
        var token = AccountCredentials.ResolveWebUserTokenForRoute(route.Account, config, route.Provider.Id);
        if (ApiRouteResolver.UsesEmbeddedWeb(config, route.Provider.Id) && string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "请先在 API 管理中为 DeepSeek 添加账户并填写用户 Token（普通对话登录不会自动同步）。");
        }

        var mcpCatalog = prep.McpCatalog ?? "";

        var useThinking = config.AgentDeepThinking;
        var useSearch = config.AgentWebSearch;

        var snapshotMax = Math.Clamp(config.AgentWorkspaceSnapshotMaxEntries, 10, 200);
        var snapshot = HarnessWorkspaceBootstrap.BuildSnapshot(workspace, snapshotMax);
        var useOpenAiTools = AgentChatClientFactory.UsesOpenAiTools(config);
        var model = string.IsNullOrWhiteSpace(config.Model) ? AgentModeHelper.AgentModel : config.Model;

        using var runTracer = HarnessRunTracer.TryBegin(
            workspace,
            state.RunId,
            config,
            new HarnessRunTracerContext
            {
                Strategy = strategy,
                SessionId = request.AgentSessionId,
                Model = model,
                PromptPreview = request.Prompt
            });
        _trace.BindTracer(runTracer);

        if (config.AgentSemanticMemoryEnabled
            && !HarnessPromptBudget.IsLikelyCasualPrompt(request.Prompt))
        {
            try
            {
                using var memStore = new HarnessSemanticMemoryStore();
                using var embed = new HarnessEmbeddingClient(config);
                var retriever = new HarnessMemoryRetriever(memStore, embed);
                var semantic = await retriever.RetrieveAsync(
                    config, request.Prompt, workspace, request.AgentSessionId, ct);
                if (semantic.Count > 0)
                {
                    memory = new HarnessMemoryContext
                    {
                        DomainId = memory.DomainId,
                        DomainName = memory.DomainName,
                        L0CoreExcerpt = memory.L0CoreExcerpt,
                        L2Behavior = memory.L2Behavior,
                        L1Context = memory.L1Context,
                        L3Cognitive = memory.L3Cognitive,
                        CheckpointSummary = memory.CheckpointSummary,
                        PendingItems = memory.PendingItems,
                        Pitfalls = memory.Pitfalls,
                        SemanticMemories = semantic
                    };
                }
            }
            catch
            {
                // 语义记忆检索失败不阻断
            }
        }

        var toolInventory = HarnessToolInventory.Build(config, mcpCatalog);
        var useHarnessIntentRouting = config.AgentAutoIntentRouting;
        HarnessRunIntent? intent = useHarnessIntentRouting
            ? HarnessIntentCache.TryRestore(request, state)
            : null;
        if (useHarnessIntentRouting && HarnessRunIntentPlanner.ShouldPlan(request, state))
        {
            callbacks.OnLog?.Invoke("正在分析任务意图并匹配 Skill / 工具…");
            using (runTracer?.StartSpan("intent.plan"))
            {
                intent = await HarnessRunIntentPlanner.PlanAsync(request, _chat, mcpCatalog, workspace, ct);
            }

            if (intent is not null)
            {
                HarnessIntentCache.Save(state, request.Prompt, intent);
                if (skill is null
                    && intent.AutoSelectedSkill
                    && !string.IsNullOrWhiteSpace(intent.SelectedSkillId)
                    && HarnessSkillRegistry.TryGet(
                        intent.SelectedSkillId, workspace, out skill, config.AgentSkillExtraRoots))
                {
                    state.SkillId = skill!.Id;
                    callbacks.OnLog?.Invoke($"已自动匹配 Skill: {skill.Name} ({skill.Id})");
                }

                callbacks.OnActivity?.Invoke(new AgentUiActivity(
                    "Intent",
                    intent.UsedLlm ? "LLM 规划" : "启发式",
                    TruncateIntent(intent.Analysis)));
            }
        }
        else if (useHarnessIntentRouting && intent is not null)
        {
            callbacks.OnActivity?.Invoke(new AgentUiActivity("Intent", "缓存", TruncateIntent(intent.Analysis)));
        }

        List<ChatMessage> messages;
        if (state.Messages is { Count: > 0 })
        {
            messages = state.Messages;
            var last = messages[^1];
            if (last.Role != "user"
                || !string.Equals(last.Content?.Trim(), request.Prompt.Trim(), StringComparison.Ordinal))
            {
                messages.Add(new ChatMessage { Role = "user", Content = request.Prompt });
            }
        }
        else
        {
            messages = HarnessComposer.BuildInitialMessages(
                request, workspace, mcpCatalog, snapshot, state.Phase, profile, playbook, memory, skill,
                intent,
                includeXmlTools: !useOpenAiTools).ToList();
            var bootstrap = HarnessSessionHistoryBootstrap.TryBuildHistory(request.AgentSessionId, request.Prompt);
            if (bootstrap is { Count: > 0 })
            {
                var systemCount = messages.TakeWhile(m =>
                    string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)).Count();
                messages.InsertRange(systemCount, bootstrap);
            }
        }

        var contextBlock = await HarnessContextAssembler.AssembleAsync(
            workspace,
            request.Prompt,
            new HarnessContextAssemblerOptions { RefPaths = request.RefFileIds },
            ct);
        if (!string.IsNullOrWhiteSpace(contextBlock))
        {
            var insertAt = messages.TakeWhile(m =>
                string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)).Count();
            messages.Insert(insertAt, new ChatMessage { Role = "system", Content = contextBlock });
        }

        await _kernel.MaybeCompactAsync(
            messages, config, model, useThinking, useSearch, runTracer, callbacks, ct);

        var blueprintRetried = false;
        var emptyExecuteRetries = 0;
        var incompleteToolRetries = 0;
        var proseOnlyExecuteRetries = 0;
        var loopGuard = new HarnessLoopGuard();

        for (var turn = 0; turn < maxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();
            state.TurnCount = turn + 1;

            if (profile.Workflow == HarnessWorkflow.Blueprint && !state.BlueprintFinalized)
            {
                if (state.Phase == HarnessPhase.Orient && turn >= 1)
                    TransitionPhase(state, HarnessPhase.Explore, callbacks, HarnessComposer.BuildOrientToExploreTransition(), messages);

                if (state.Phase == HarnessPhase.Explore && turn >= researchCap)
                    TransitionPhase(state, HarnessPhase.Blueprint, callbacks, HarnessComposer.BuildBlueprintFinalizeUserMessage(), messages, blueprintFinalized: true);
            }

            var allowTools = HarnessPhasePolicy.AllowsTools(state.Phase, state.BlueprintFinalized);
            _trace.Turn(state.TurnCount, HarnessPhasePolicy.TraceLabel(state.Phase));
            callbacks.OnLog?.Invoke($"第 {state.TurnCount} 轮：正在请求模型…");
            AgentDebugLogger.Current?.Write("HARNESS",
                $"stream: turn {state.TurnCount} begin thinking={useThinking} search={useSearch} tools={allowTools}");

            var chatOptions = await HarnessOpenAiToolLoop.BuildChatOptionsAsync(
                config, _mcp, allowTools, ct, intent, toolInventory, state.Phase);

            var result = await StreamOneTurnAsync(
                messages, model, config, request.RefFileIds, allowTools, useThinking, useSearch, token, webSessionId,
                chatOptions, callbacks, runTracer, ct);

            if (!string.IsNullOrWhiteSpace(result.ChatSessionId))
            {
                webSessionId = result.ChatSessionId;
                state.WebChatSessionId = webSessionId;
            }

            if (!string.IsNullOrWhiteSpace(result.ReasoningContent))
                callbacks.OnThinking?.Invoke(result.ReasoningContent, false);

            var toolCalls = result.ToolCalls;
            var answerText = HarnessAnswerDisplayFilter.StripForDisplay(result.Content);
            if (toolCalls is not { Count: > 0 } && allowTools)
            {
                var parseSource = result.Content;
                if (HarnessEmptyReply.IsEmpty(parseSource)
                    && !string.IsNullOrWhiteSpace(result.ReasoningContent))
                    parseSource = result.ReasoningContent;

                if (!string.IsNullOrWhiteSpace(parseSource))
                {
                    var parsed = HarnessXmlToolCallParser.TryParse(parseSource, out var stripped);
                    if (parsed.Count > 0)
                    {
                        toolCalls = parsed.ToList();
                        answerText = HarnessAnswerDisplayFilter.StripForDisplay(stripped);
                        callbacks.OnLog?.Invoke("已从回复文本解析 " + parsed.Count + " 个工具调用（对齐 DeepSeek-TUI tool_parser）");
                    }
                    else if ((HarnessXmlToolCallParser.HasToolCallMarkers(parseSource)
                              || HarnessXmlToolCallParser.HasIncompleteToolArtifacts(parseSource))
                             && incompleteToolRetries < 2)
                    {
                        incompleteToolRetries++;
                        if (!HarnessEmptyReply.IsEmpty(answerText))
                            messages.Add(new ChatMessage { Role = "assistant", Content = answerText });
                        messages.Add(HarnessComposer.BuildIncompleteToolCallRetryMessage());
                        callbacks.OnLog?.Invoke("检测到未完成的工具调用文本，请求模型输出 <tool_calling> (" + incompleteToolRetries + "/2)");
                        continue;
                    }
                }
            }

            if (toolCalls is not { Count: > 0 })
            {
                var text = (answerText ?? "").Trim();

                if (HarnessEmptyReply.IsEmpty(text)
                    && allowTools
                    && emptyExecuteRetries < 2)
                {
                    emptyExecuteRetries++;
                    if (!HarnessEmptyReply.IsEmpty(text))
                        messages.Add(new ChatMessage { Role = "assistant", Content = text });
                    messages.Add(HarnessComposer.BuildEmptyExecuteRetryMessage(request.Prompt));
                    callbacks.OnLog?.Invoke("模型空回复，自动追问 (" + emptyExecuteRetries + "/2)");
                    AgentDebugLogger.Current?.Write("HARNESS", "empty execute reply → retry " + emptyExecuteRetries);
                    continue;
                }

                if (profile.Workflow == HarnessWorkflow.Execute
                    && state.Phase == HarnessPhase.Execute
                    && allowTools
                    && proseOnlyExecuteRetries < 2
                    && HarnessExecuteReplyGuard.IsProseOnlyTaskAnalysis(text, request.Prompt))
                {
                    proseOnlyExecuteRetries++;
                    messages.Add(new ChatMessage { Role = "assistant", Content = text });
                    messages.Add(HarnessComposer.BuildExecuteProseOnlyRetryMessage());
                    callbacks.OnLog?.Invoke("仅有任务分析、未调用工具，自动追问 (" + proseOnlyExecuteRetries + "/2)");
                    AgentDebugLogger.Current?.Write("HARNESS", "prose-only execute → retry " + proseOnlyExecuteRetries);
                    continue;
                }

                if (string.IsNullOrEmpty(text))
                    text = "（无回复内容）";

                if (profile.Workflow == HarnessWorkflow.Blueprint
                    && state.Phase == HarnessPhase.Orient
                    && !state.BlueprintFinalized)
                {
                    messages.Add(new ChatMessage { Role = "assistant", Content = text });
                    TransitionPhase(state, HarnessPhase.Explore, callbacks, HarnessComposer.BuildOrientToExploreTransition(), messages);
                    continue;
                }

                if (profile.Workflow == HarnessWorkflow.Blueprint
                    && state.Phase == HarnessPhase.Explore
                    && !state.BlueprintFinalized
                    && turn < researchCap)
                {
                    if (HasExploreToolEvidence(messages))
                    {
                        messages.Add(new ChatMessage { Role = "assistant", Content = text });
                        TransitionPhase(state, HarnessPhase.Blueprint, callbacks, HarnessComposer.BuildBlueprintFinalizeUserMessage(), messages, blueprintFinalized: true);
                        continue;
                    }

                    state.Messages = messages;
                    return await FinalizeRunAsync(
                        text, state, webSessionId, domain, request, config, workspace, runTracer, ct);
                }

                if (state.Phase == HarnessPhase.Blueprint && state.BlueprintFinalized && !blueprintRetried)
                {
                    var validation = HarnessSelfValidator.ValidateBlueprint(text);
                    if (!validation.Passed)
                    {
                        blueprintRetried = true;
                        messages.Add(new ChatMessage { Role = "assistant", Content = text });
                        messages.Add(HarnessSelfValidator.BuildBlueprintRetryMessage(validation.Issues));
                        callbacks.OnLog?.Invoke("自检: Blueprint 结构不完整，请求重写");
                        continue;
                    }
                }

                if (profile.Workflow == HarnessWorkflow.Execute
                    && state.Phase == HarnessPhase.Execute
                    && !state.VerifyCompleted)
                {
                    var verifySteps = HarnessVerifyChain.Resolve(playbook, config);
                    if (verifySteps.Count > 0)
                    {
                        var verifyResult = await RunVerifyPhaseAsync(
                            text, verifySteps, workspace, state, messages, model, config,
                            request.RefFileIds, useThinking, useSearch, token, webSessionId, callbacks, runTracer, ct);
                        state.Messages = messages;
                        return await FinalizeRunAsync(
                            verifyResult.Answer, state, webSessionId, domain, request, config, workspace, runTracer, ct);
                    }
                }

                state.Messages = messages;
                return await FinalizeRunAsync(
                    text, state, webSessionId, domain, request, config, workspace, runTracer, ct);
            }

            if (!allowTools)
            {
                var fallback = (result.Content ?? "").Trim();
                if (string.IsNullOrEmpty(fallback))
                    fallback = "（模型在 " + HarnessPhasePolicy.TraceLabel(state.Phase) + " 阶段仍尝试调用工具，已忽略）";
                state.Messages = messages;
                return await FinalizeRunAsync(
                    fallback, state, webSessionId, domain, request, config, workspace, runTracer, ct);
            }

            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = result.Content,
                ToolCalls = toolCalls
            });

            var halt = await ExecuteToolCallsForTurnAsync(
                toolCalls, messages, config, workspace, state, sandboxCoord, loopGuard, callbacks, ct);
            if (halt is not null)
            {
                state.Messages = messages;
                return await FinalizeRunAsync(
                    halt, state, webSessionId, domain, request, config, workspace, runTracer, ct);
            }

            state.Messages = messages;
        }

        state.Messages = messages;
        return await FinalizeRunAsync(
            "已达到最大工具调用轮数，请缩小任务或重试。", state, webSessionId, domain, request, config, workspace, runTracer, ct);
    }

    private async Task<HarnessRunResult> FinalizeRunAsync(
        string answer,
        HarnessRunState state,
        string? webSessionId,
        HarnessDomainMatch domain,
        HarnessRunRequest request,
        AppConfig config,
        string workspace,
        HarnessRunTracer? runTracer,
        CancellationToken ct)
    {
        CommitAssistantTurn(state, HarnessAnswerDisplayFilter.StripForDisplay(answer));
        var userPrompt = request.Prompt;
        var wrotePostMortem = false;
        try
        {
            HarnessCheckpointStore.UpdateAfterRun(
                userPrompt, answer, domain, state.Phase, state.BlueprintFinalized);
        }
        catch
        {
            // 检查点写入失败不阻断任务
        }

        if (config.AgentWritePostMortem && !string.IsNullOrWhiteSpace(state.RunId))
        {
            HarnessPostMortemWriter.Write(workspace, state.RunId, domain, userPrompt, answer, state);
            wrotePostMortem = true;
        }

        runTracer?.FinalizeRun(new HarnessRunMetaFinalizeArgs
        {
            WorkspaceRoot = workspace,
            Strategy = request.Strategy,
            SessionId = request.AgentSessionId,
            Model = config.Model,
            PromptPreview = userPrompt,
            AnswerPreview = answer,
            Phase = HarnessPhasePolicy.TraceLabel(state.Phase),
            DomainId = domain.Id,
            WrotePostMortem = wrotePostMortem,
            RetentionDays = config.AgentTraceRetentionDays
        });

        if (config.AgentSemanticMemoryEnabled)
        {
            try
            {
                HarnessMemoryMaintenance.PruneExpiredSessions(config);
                if (config.AgentSemanticMemoryAutoExtract)
                {
                    using var memStore = new HarnessSemanticMemoryStore();
                    using var embed = new HarnessEmbeddingClient(config);
                    var extractor = new HarnessMemoryExtractor(memStore, embed, () => _chat);
                    await extractor.ExtractAfterRunAsync(
                        config, userPrompt, answer, workspace, request.AgentSessionId, ct);
                }
            }
            catch
            {
                // 抽取失败不阻断
            }
        }

        AgentNotifyRunner.TryLaunch(config, TruncateNotify(userPrompt, answer));

        var displayAnswer = HarnessAnswerDisplayFilter.StripForDisplay(answer);
        var (patchAccepted, patchRejected, patchRate) = HarnessPatchMetrics.Snapshot();
        return new HarnessRunResult
        {
            Answer = displayAnswer,
            HarnessState = SerializeState(state),
            WebChatSessionId = webSessionId,
            LastCheckpointHash = _tools.LastCheckpointHash ?? state.LastCheckpointHash,
            RunId = state.RunId,
            DurationMs = runTracer?.ElapsedMilliseconds ?? 0,
            PromptTokens = runTracer?.PromptTokens ?? 0,
            CompletionTokens = runTracer?.CompletionTokens ?? 0,
            LlmCallCount = runTracer?.LlmCallCount ?? 0,
            ToolCallCount = runTracer?.ToolCallCount ?? 0,
            PatchesAccepted = patchAccepted,
            PatchesRejected = patchRejected,
            PatchAcceptRate = patchRate
        };
    }

    private async Task<HarnessRunResult> RunVerifyPhaseAsync(
        string executeAnswer,
        IReadOnlyList<HarnessVerifyStep> verifySteps,
        string workspace,
        HarnessRunState state,
        List<ChatMessage> messages,
        string model,
        AppConfig config,
        IReadOnlyList<string> refFileIds,
        bool thinking,
        bool search,
        string? token,
        string? webSessionId,
        HarnessRunCallbacks callbacks,
        HarnessRunTracer? runTracer,
        CancellationToken ct)
    {
        TransitionPhase(state, HarnessPhase.Verify, callbacks, null, messages);
        callbacks.OnLog?.Invoke("Verify 链: " + verifySteps.Count + " 步");

        var chain = await HarnessVerifyChain.RunAsync(verifySteps, workspace, ct);
        state.VerifyCompleted = true;

        messages.Add(new ChatMessage { Role = "assistant", Content = executeAnswer });
        messages.Add(HarnessComposer.BuildVerifyUserMessage(chain.CombinedOutput, chain.Passed));

        var summary = await StreamOneTurnAsync(
            messages, model, config, refFileIds, allowToolCalls: false, thinking, search, token, webSessionId,
            new AgentChatOptions { UseOpenAiTools = false }, callbacks, runTracer, ct);

        var finalText = (summary.Content ?? "").Trim();
        if (string.IsNullOrEmpty(finalText))
        {
            finalText = executeAnswer + "\n\n## Verify\n" + chain.CombinedOutput;
            if (chain.AnyRequiredFailed)
                finalText += "\n\n**Verify 未通过（必需步骤失败）**";
        }

        return new HarnessRunResult
        {
            Answer = finalText,
            WebChatSessionId = webSessionId
        };
    }

    private static void TransitionPhase(
        HarnessRunState state,
        HarnessPhase phase,
        HarnessRunCallbacks callbacks,
        ChatMessage? message,
        List<ChatMessage> messages,
        bool blueprintFinalized = false)
    {
        state.Phase = phase;
        if (blueprintFinalized)
            state.BlueprintFinalized = true;
        if (message is not null)
            messages.Add(message);
        NotifyPhase(phase, callbacks);
    }

    private static void NotifyPhase(HarnessPhase phase, HarnessRunCallbacks callbacks) =>
        callbacks.OnPhaseChanged?.Invoke(phase);

    private static string TruncateNotify(string prompt, string answer)
    {
        var text = prompt + " => " + answer;
        return text.Length <= 500 ? text : text[..500] + "…";
    }

    private async Task<WebChatResult> StreamOneTurnAsync(
        List<ChatMessage> messages,
        string model,
        AppConfig config,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        bool thinking,
        bool search,
        string? token,
        string? webSessionId,
        AgentChatOptions? chatOptions,
        HarnessRunCallbacks callbacks,
        HarnessRunTracer? runTracer,
        CancellationToken ct)
    {
        const int maxStreamRetries = 3;
        WebChatResult? last = null;
        for (var attempt = 0; attempt < maxStreamRetries; attempt++)
        {
            try
            {
                last = await StreamOneTurnCoreAsync(
                    messages, model, config, refFileIds, allowToolCalls, thinking, search, token,
                    webSessionId, chatOptions, callbacks, runTracer, ct);
            }
            catch (InvalidOperationException) when (attempt < maxStreamRetries - 1)
            {
                callbacks.OnLog?.Invoke($"流式连接中断，重试 ({attempt + 1}/{maxStreamRetries})…");
                continue;
            }

            var hasTools = last.ToolCalls is { Count: > 0 };
            var hasText = !HarnessEmptyReply.IsEmpty(last.Content)
                          || !string.IsNullOrWhiteSpace(last.ReasoningContent);
            if (hasTools || hasText || attempt == maxStreamRetries - 1)
                return last;

            callbacks.OnLog?.Invoke($"流式无有效输出，重试 ({attempt + 1}/{maxStreamRetries})…");
        }

        return last!;
    }

    private async Task<WebChatResult> StreamOneTurnCoreAsync(
        List<ChatMessage> messages,
        string model,
        AppConfig config,
        IReadOnlyList<string> refFileIds,
        bool allowToolCalls,
        bool thinking,
        bool search,
        string? token,
        string? webSessionId,
        AgentChatOptions? chatOptions,
        HarnessRunCallbacks callbacks,
        HarnessRunTracer? runTracer,
        CancellationToken ct)
    {
        using var llmSpan = runTracer?.StartSpan("llm.completion", null, new Dictionary<string, object?>
        {
            ["model"] = model,
            ["allowTools"] = allowToolCalls
        });

        var answerBuilder = new StringBuilder();
        var lastDisplayLen = 0;
        WebChatResult? result = null;

        await foreach (var ev in _chat.StreamAsync(
                           messages,
                           model,
                           thinking,
                           search,
                           refFileIds,
                           allowToolCalls,
                           ct,
                           token,
                           webSessionId,
                           chatOptions))
        {
            switch (ev)
            {
                case WebChatStreamDelta delta when delta.Kind is WebChatStreamDelta.Reasoning or "thinking":
                    callbacks.OnThinking?.Invoke(delta.Text, true);
                    break;
                case WebChatStreamDelta delta when delta.Kind == "content":
                    answerBuilder.Append(delta.Text);
                    var display = HarnessAnswerDisplayFilter.StripForDisplay(answerBuilder.ToString());
                    if (display.Length > lastDisplayLen)
                    {
                        callbacks.OnAnswerDelta?.Invoke(display[lastDisplayLen..], true);
                        lastDisplayLen = display.Length;
                    }
                    else if (display.Length < lastDisplayLen)
                    {
                        callbacks.OnAnswerDelta?.Invoke(display, false);
                        lastDisplayLen = display.Length;
                    }
                    break;
                case WebChatStreamDelta delta when delta.Kind == "tool":
                    callbacks.OnActivity?.Invoke(new AgentUiActivity(
                        "Running",
                        "`" + HarnessOpenAiToolLoop.NormalizeToolName(delta.Text) + "`",
                        null));
                    break;
                case WebChatStreamDone done:
                    result = done.Result;
                    break;
                case WebChatStreamError err:
                    throw new InvalidOperationException(err.Message);
            }
        }

        result ??= await _chat.CompleteAsync(
            messages,
            model,
            thinking,
            search,
            refFileIds,
            allowToolCalls,
            ct,
            token,
            webSessionId,
            chatOptions);

        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(result.Content) && answerBuilder.Length > 0)
        {
            result = new WebChatResult
            {
                Content = answerBuilder.ToString(),
                ChatSessionId = result.ChatSessionId,
                ReasoningContent = result.ReasoningContent,
                ToolCalls = result.ToolCalls,
                Model = result.Model,
                FinishReason = result.FinishReason,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                TotalTokens = result.TotalTokens
            };
        }

        if (!AgentChatClientFactory.UsesDirectApi(config)
            && HarnessEmptyReply.IsEmpty(result.Content)
            && string.IsNullOrWhiteSpace(result.ReasoningContent)
            && (result.ToolCalls is null or { Count: 0 }))
        {
            AgentDebugLogger.Current?.Write("HARNESS",
                "web empty completion → non-stream fallback model=" + model
                + " modelType=" + WebChatBridgeModelTypes.Resolve(model)
                + " finish=" + (result.FinishReason ?? ""));
            callbacks.OnLog?.Invoke("网页流式无正文，改用非流式补发…");
            result = await _chat.CompleteAsync(
                messages,
                model,
                thinking,
                search,
                refFileIds,
                allowToolCalls,
                ct,
                token,
                webSessionId,
                chatOptions);
        }

        var inferenceSource = AgentChatClientFactory.UsesDirectApi(config) ? "api" : "web";
        runTracer?.RecordLlmUsage(result, inferenceSource);
        llmSpan?.SetAttribute("finishReason", result.FinishReason ?? "");
        llmSpan?.SetAttribute("promptTokens", result.PromptTokens);
        llmSpan?.SetAttribute("completionTokens", result.CompletionTokens);

        return result;
    }

    private async Task<string?> ExecuteToolCallsForTurnAsync(
        IReadOnlyList<WebToolCall> toolCalls,
        List<ChatMessage> messages,
        AppConfig config,
        string workspace,
        HarnessRunState state,
        HarnessSandboxCoordinator? sandboxCoord,
        HarnessLoopGuard loopGuard,
        HarnessRunCallbacks callbacks,
        CancellationToken ct)
    {
        _tools.SetRunCallbacks(callbacks);
        var indexed = toolCalls.Select((tc, i) => (Index: i, Call: tc)).ToList();
        var parallel = indexed
            .Where(x => HarnessParallelToolPolicy.CanRunInParallel(x.Call.Name, state.Phase))
            .ToList();
        var serial = indexed
            .Where(x => !HarnessParallelToolPolicy.CanRunInParallel(x.Call.Name, state.Phase))
            .ToList();

        if (parallel.Count > 1)
            callbacks.OnLog?.Invoke($"并行执行 {parallel.Count} 个工具…");

        var toolMessages = new ChatMessage?[toolCalls.Count];
        var followUpLists = new List<ChatMessage>[toolCalls.Count];

        async Task<string?> ProcessOneAsync(int index, WebToolCall tc)
        {
            if (!loopGuard.TryRecordAttempt(tc.Name, tc.Arguments, out var blockMsg))
            {
                toolMessages[index] = new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = blockMsg
                };
                return null;
            }

            callbacks.OnActivity?.Invoke(
                HarnessActivityMapper.MapToolCall(tc.Name, tc.Arguments, workspace));
            callbacks.OnLog?.Invoke("工具: " + tc.Name);

            var execName = AgentChatClientFactory.UsesOpenAiTools(config)
                ? HarnessOpenAiToolLoop.NormalizeToolName(tc.Name)
                : tc.Name;

            var execArgs = HarnessXmlToolCallParser.NormalizeWriteFileArguments(execName, tc.Arguments);

            var execResult = await _tools.ExecuteDetailedAsync(
                execName, execArgs, config, workspace, state.Phase, ct, sandboxCoord,
                chunk => callbacks.OnShellOutput?.Invoke(chunk));

            var ok = !execResult.Output.StartsWith("ERROR:", StringComparison.Ordinal);
            if (execResult.PendingPatch is { } pending)
                callbacks.OnPendingPatch?.Invoke(pending);
            if (execResult.FilePreview is { } fp)
                callbacks.OnFilePreview?.Invoke(fp);
            else if (ok && HarnessFilePreview.TryBuildAfterTool(execName, execArgs, workspace, true, out var preview))
                callbacks.OnFilePreview?.Invoke(preview);

            var outcome = loopGuard.RecordOutcome(tc.Name, ok);
            if (outcome.Kind == HarnessLoopGuard.OutcomeDecisionKind.Warn)
                callbacks.OnLog?.Invoke(outcome.Message ?? "");
            if (outcome.Kind == HarnessLoopGuard.OutcomeDecisionKind.Halt)
                return outcome.Message ?? "工具连续失败，已停止本轮。";

            var toolResult = execResult.Output;
            if (ok && (execName.Contains("edit", StringComparison.OrdinalIgnoreCase)
                       || execName.Contains("write", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var doc = JsonDocument.Parse(execArgs);
                    var rel = doc.RootElement.TryGetProperty("file_path", out var fpEl) ? fpEl.GetString()
                              : doc.RootElement.TryGetProperty("path", out var p) ? p.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(rel))
                    {
                        var diags = await HarnessLspClient.GetDiagnosticsAsync(workspace, rel, ct);
                        var errCount = diags.Count(d =>
                            d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
                        if (errCount > 0)
                            toolResult += "\n\n" + HarnessLspClient.FormatForContext(diags);
                    }
                }
                catch
                {
                    // LSP optional
                }
            }

            if (config.AgentToolOutputSpill)
            {
                toolResult = HarnessToolOutputSpill.Process(
                    tc.Name, toolResult, workspace, state.RunId!,
                    Math.Clamp(config.AgentToolOutputInlineMaxChars, 1000, 50_000));
            }

            toolMessages[index] = new ChatMessage
            {
                Role = "tool",
                ToolCallId = tc.Id,
                Content = toolResult
            };

            followUpLists[index] = [];
            foreach (var followUp in execResult.FollowUpMessages)
            {
                if (!AgentChatClientFactory.UsesDirectApi(config)
                    && followUp.ContentParts?.Any(p =>
                        string.Equals(p.Type, "image_url", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    followUpLists[index].Add(new ChatMessage
                    {
                        Role = "system",
                        Content = (followUp.ContentParts?.FirstOrDefault(p =>
                            string.Equals(p.Type, "text", StringComparison.OrdinalIgnoreCase))?.Text ?? "")
                                  + "\n[Image loaded — use API inference mode or image_analyze in web mode.]"
                    });
                }
                else
                {
                    followUpLists[index].Add(followUp);
                }
            }

            return null;
        }

        if (parallel.Count > 0)
        {
            var haltMessages = await Task.WhenAll(
                parallel.Select(x => ProcessOneAsync(x.Index, x.Call)));
            var halt = haltMessages.FirstOrDefault(h => h is not null);
            if (halt is not null)
                return halt;
        }

        foreach (var x in serial.OrderBy(x => x.Index))
        {
            var halt = await ProcessOneAsync(x.Index, x.Call);
            if (halt is not null)
                return halt;
        }

        for (var i = 0; i < toolCalls.Count; i++)
        {
            if (toolMessages[i] is not null)
                messages.Add(toolMessages[i]!);
            if (followUpLists[i] is { Count: > 0 } extra)
            {
                foreach (var fu in extra)
                    messages.Add(fu);
            }
        }

        return null;
    }

    private static void CommitAssistantTurn(HarnessRunState state, string answer)
    {
        var trimmed = (answer ?? "").Trim();
        if (trimmed.Length == 0 || state.Messages is not { Count: > 0 } messages)
            return;

        var last = messages[^1];
        if (string.Equals(last.Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && string.Equals(last.Content?.Trim(), trimmed, StringComparison.Ordinal))
        {
            return;
        }

        messages.Add(new ChatMessage { Role = "assistant", Content = trimmed });
    }

    private static HarnessRunState? DeserializeState(string? json, HarnessStrategyProfile profile)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var state = JsonSerializer.Deserialize<HarnessRunState>(json, StateJson);
            if (state == null) return null;

            var root = doc.RootElement;
            if (!root.TryGetProperty("blueprintFinalized", out _)
                && root.TryGetProperty("planFinalized", out var legacyFinalized))
            {
                state.BlueprintFinalized = legacyFinalized.GetBoolean();
            }

            if (!root.TryGetProperty("phase", out _))
            {
                if (state.BlueprintFinalized)
                    state.Phase = HarnessPhase.Blueprint;
                else if (profile.Workflow == HarnessWorkflow.Blueprint)
                    state.Phase = profile.InitialPhase;
            }

            return state;
        }
        catch
        {
            return null;
        }
    }

    private static string SerializeState(HarnessRunState state) =>
        JsonSerializer.Serialize(state, StateJson);

    private static bool HasExploreToolEvidence(IReadOnlyList<ChatMessage> messages) =>
        messages.Any(m => string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase));

    private static string TruncateIntent(string text) =>
        text.Length <= 120 ? text : text[..120] + "…";
}
