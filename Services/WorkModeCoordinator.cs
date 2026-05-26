using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 工作模式后端状态机：唯一数据源，向前端推送 <c>workModeState</c>，接收 <c>setWorkMode</c> / <c>toggleWorkMode</c> 意图。
/// </summary>
public sealed class WorkModeCoordinator
{
    private readonly IDesktopWebHost _webHost;
    private string _mode = "chat";
    private int _revision;
    private CancellationTokenSource? _retryCts;

    public WorkModeCoordinator(IDesktopWebHost webHost) => _webHost = webHost;

    public string Mode => _mode;

    public bool IsAgentLike => _mode is "agent" or "plan";

    public void LoadFromConfig(AppConfig config) => _mode = NormalizeMode(config.DefaultWorkMode);

    public void SetModeFromConfig(string? mode) => _mode = NormalizeMode(mode);

    public static string NormalizeMode(string? mode) => WorkModeModes.NormalizeMode(mode);

    public WorkModeStatePayload BuildState() =>
        WorkModeStatePayload.For(_mode, _webHost.IsAgentVisible, _revision);

    public Task BroadcastAsync(CancellationToken ct = default)
    {
        CancelScheduledRetries();
        return _webHost.BroadcastWorkModeStateAsync(ct);
    }

    public Task BroadcastImmediateAsync(CancellationToken ct = default)
    {
        _revision++;
        return _webHost.PushWorkModeStateNowAsync(BuildState(), ct);
    }

    public void ScheduleBroadcastRetries(CancellationToken ct = default)
    {
        CancelScheduledRetries();
        _retryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _webHost.ScheduleWorkModeBroadcastRetries(_retryCts.Token);
    }

    public void CancelScheduledRetries()
    {
        try
        {
            _retryCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        _retryCts?.Dispose();
        _retryCts = null;
    }

    public Task ShowChatSurfaceAsync(CancellationToken ct = default)
    {
        _webHost.ShowChat();
        return Task.CompletedTask;
    }

    public Task ShowAgentSurfaceAsync(CancellationToken ct = default)
    {
        _webHost.ShowAgent();
        return Task.CompletedTask;
    }

    /// <summary>与浮层按钮一致：按当前可见页面切换，而非仅按内存中的 mode（避免 surface/mode 不同步时点击无效）。</summary>
    public string ToggleTargetMode() => WorkModeModes.ToggleTargetMode(_webHost.IsAgentVisible);
}
