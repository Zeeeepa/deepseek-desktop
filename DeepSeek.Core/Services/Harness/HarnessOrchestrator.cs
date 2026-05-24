using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness.Interop;
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

    public HarnessOrchestrator(IAgentWebChat chat, McpHub mcp, ApprovalGate approval)
    {
        _chat = chat;
        _mcp = mcp;
        _tools = new HarnessToolExecutor(mcp, approval, _trace);
    }

    private readonly HarnessToolExecutor _tools;

    public async Task<HarnessRunResult> RunAsync(
        HarnessRunRequest request,
        HarnessRunCallbacks callbacks,
        CancellationToken ct)
    {
        var config = request.Config;
        var workspace = AgentWorkspace.ResolveRoot(config);
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
        var maxTurns = Math.Clamp(config.MaxAgentSteps, 1, 50);
        var researchCap = HarnessPhasePolicy.ResearchCap(profile.Workflow, maxTurns);

        var state = DeserializeState(request.ExistingHarnessState, profile) ?? new HarnessRunState
        {
            Phase = profile.InitialPhase
        };

        await using var sandboxCoord = await HarnessSandboxCoordinator.BeginRunAsync(
            state, config, workspace, _trace, callbacks.OnLog, ct);

        callbacks.OnLog?.Invoke("正在准备对话上下文…");
        AgentDebugLogger.Current?.Write("HARNESS", "prep: memory + MCP catalog");

        var memory = HarnessMemoryLoader.Load(request.Prompt, workspace);
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
        var token = config.WebUserToken;

        string? mcpCatalog = null;
        try
        {
            using var mcpTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            mcpTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            mcpCatalog = await _mcp.BuildToolCatalogTextAsync(mcpTimeout.Token);
            var toolLineCount = mcpCatalog.Count(c => c == '\n');
            AgentDebugLogger.Current?.Write("HARNESS", $"prep: MCP catalog lines={toolLineCount}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            mcpCatalog = "已连接的 MCP 工具（调用名须完全一致）：\n（工具目录加载超时，本轮可能无法调用 MCP）";
            callbacks.OnLog?.Invoke("MCP 工具目录加载超时，继续请求模型…");
            AgentDebugLogger.Current?.Write("HARNESS", "prep: MCP catalog timeout (5s)");
        }
        catch (Exception ex)
        {
            mcpCatalog = "已连接的 MCP 工具（调用名须完全一致）：\n（无法读取 MCP 工具列表）";
            AgentDebugLogger.Current?.Write("HARNESS", "prep: MCP catalog error " + ex.Message);
        }

        var useThinking = config.AgentDeepThinking;
        var useSearch = config.AgentWebSearch;

        var snapshot = HarnessWorkspaceBootstrap.BuildSnapshot(workspace);
        List<ChatMessage> messages;
        if (state.Messages is { Count: > 0 })
        {
            messages = state.Messages;
            messages.Add(new ChatMessage { Role = "user", Content = request.Prompt });
        }
        else
        {
            messages = HarnessComposer.BuildInitialMessages(
                request, workspace, mcpCatalog, snapshot, state.Phase, profile, playbook, memory, skill).ToList();
        }

        var model = string.IsNullOrWhiteSpace(config.Model) ? AgentModeHelper.AgentModel : config.Model;
        var blueprintRetried = false;

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

            var result = await StreamOneTurnAsync(
                messages, model, config, request.RefFileIds, allowTools, useThinking, useSearch, token, webSessionId,
                callbacks, ct);

            if (!string.IsNullOrWhiteSpace(result.ChatSessionId))
            {
                webSessionId = result.ChatSessionId;
                state.WebChatSessionId = webSessionId;
            }

            if (!string.IsNullOrWhiteSpace(result.ReasoningContent))
                callbacks.OnThinking?.Invoke(result.ReasoningContent, false);

            var toolCalls = result.ToolCalls;
            if (toolCalls is not { Count: > 0 })
            {
                var text = (result.Content ?? "").Trim();
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
                    return FinalizeRun(text, state, webSessionId, domain, request.Prompt, config, workspace);
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
                            request.RefFileIds, useThinking, useSearch, token, webSessionId, callbacks, ct);
                        state.Messages = messages;
                        return FinalizeRun(verifyResult.Answer, state, webSessionId, domain, request.Prompt, config, workspace);
                    }
                }

                state.Messages = messages;
                return FinalizeRun(text, state, webSessionId, domain, request.Prompt, config, workspace);
            }

            if (!allowTools)
            {
                var fallback = (result.Content ?? "").Trim();
                if (string.IsNullOrEmpty(fallback))
                    fallback = "（模型在 " + HarnessPhasePolicy.TraceLabel(state.Phase) + " 阶段仍尝试调用工具，已忽略）";
                state.Messages = messages;
                return FinalizeRun(fallback, state, webSessionId, domain, request.Prompt, config, workspace);
            }

            messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = result.Content,
                ToolCalls = toolCalls
            });

            foreach (var tc in toolCalls)
            {
                callbacks.OnActivity?.Invoke(
                    HarnessActivityMapper.MapToolCall(tc.Name, tc.Arguments, workspace));
                callbacks.OnLog?.Invoke("工具: " + tc.Name);

                var toolResult = await _tools.ExecuteAsync(
                    tc.Name, tc.Arguments, config, workspace, state.Phase, ct, sandboxCoord);

                if (config.AgentToolOutputSpill)
                {
                    toolResult = HarnessToolOutputSpill.Process(
                        tc.Name, toolResult, workspace, state.RunId!,
                        Math.Clamp(config.AgentToolOutputInlineMaxChars, 1000, 50_000));
                }

                messages.Add(new ChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = toolResult
                });
            }

            state.Messages = messages;
        }

        state.Messages = messages;
        return FinalizeRun("已达到最大工具调用轮数，请缩小任务或重试。", state, webSessionId, domain, request.Prompt, config, workspace);
    }

    private HarnessRunResult FinalizeRun(
        string answer,
        HarnessRunState state,
        string? webSessionId,
        HarnessDomainMatch domain,
        string userPrompt,
        AppConfig config,
        string workspace)
    {
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
        }

        return new HarnessRunResult
        {
            Answer = answer,
            HarnessState = SerializeState(state),
            WebChatSessionId = webSessionId
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
            callbacks, ct);

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
        HarnessRunCallbacks callbacks,
        CancellationToken ct)
    {
        var answerBuilder = new StringBuilder();
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
                           webSessionId))
        {
            switch (ev)
            {
                case WebChatStreamDelta delta when delta.Kind == "thinking":
                    callbacks.OnThinking?.Invoke(delta.Text, true);
                    break;
                case WebChatStreamDelta delta when delta.Kind == "content":
                    callbacks.OnAnswerDelta?.Invoke(delta.Text, true);
                    answerBuilder.Append(delta.Text);
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
            webSessionId);

        if (string.IsNullOrWhiteSpace(result.Content) && answerBuilder.Length > 0)
        {
            result = new WebChatResult
            {
                Content = answerBuilder.ToString(),
                ChatSessionId = result.ChatSessionId,
                ReasoningContent = result.ReasoningContent,
                ToolCalls = result.ToolCalls,
                Model = result.Model,
                FinishReason = result.FinishReason
            };
        }

        return result;
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
}
