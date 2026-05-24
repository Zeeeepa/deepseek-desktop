using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness;

public sealed class HarnessRunRequest
{
    public required AppConfig Config { get; init; }
    public required string Prompt { get; init; }
    public required string Strategy { get; init; }
    public string? ExistingHarnessState { get; init; }
    public string? WebChatSessionId { get; init; }
    public IReadOnlyList<string> RefFileIds { get; init; } = Array.Empty<string>();
    public string? PlaybookId { get; init; }
    public string? SkillId { get; init; }
}

public sealed class HarnessRunResult
{
    public string Answer { get; init; } = "";
    public string? HarnessState { get; init; }
    public string? WebChatSessionId { get; init; }
}

public sealed class HarnessRunCallbacks
{
    public Action<string>? OnLog { get; init; }
    public Action<string, bool>? OnThinking { get; init; }
    public Action<string, bool>? OnAnswerDelta { get; init; }
    public Action<AgentUiActivity>? OnActivity { get; init; }
    public Action<HarnessPhase>? OnPhaseChanged { get; init; }
}

public sealed class HarnessRunState
{
    public string? WebChatSessionId { get; set; }
    public int TurnCount { get; set; }
    public HarnessPhase Phase { get; set; } = HarnessPhase.Execute;
    public bool BlueprintFinalized { get; set; }
    public List<ChatMessage>? Messages { get; set; }
    public string? PlaybookId { get; set; }
    public bool VerifyCompleted { get; set; }
    public string? DomainId { get; set; }
    public string? SkillId { get; set; }
    public string? RunId { get; set; }
    public string? SandboxSessionId { get; set; }
    public Sandbox.HarnessSandboxKind SandboxKind { get; set; } = Sandbox.HarnessSandboxKind.Local;
}
