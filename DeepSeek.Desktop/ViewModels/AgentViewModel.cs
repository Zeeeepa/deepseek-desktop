using System.Collections.ObjectModel;
using DeepSeek.Desktop.Services;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeek.Desktop.ViewModels;

public sealed class AgentMessageItem
{
    public string Role { get; init; } = "";
    public string Text { get; init; } = "";
    public string? Kind { get; init; }
}

public sealed class AgentViewModel
{
    private readonly NativeAgentService _agent = new();
    private CancellationTokenSource? _runCts;

    public ObservableCollection<AgentMessageItem> Messages { get; } = new();
    public string InputText { get; set; } = "";
    public bool IsRunning { get; private set; }
    public bool DeepThinking { get; set; } = true;
    public bool WebSearch { get; set; }
    public string Strategy { get; set; } = AgentStrategies.Execute;

    public event Action? StateChanged;

    public AgentViewModel()
    {
        _agent.OnMessage += (_, e) => Ui(() => Messages.Add(new AgentMessageItem
        {
            Role = e.Role,
            Text = e.Text,
            Kind = e.Kind
        }));
        _agent.OnStreamDelta += (_, e) => Ui(() =>
        {
            var role = e.IsThinking ? "thinking" : "assistant";
            if (Messages.Count > 0 && Messages[^1].Role == role)
            {
                var last = Messages[^1];
                Messages[^1] = new AgentMessageItem
                {
                    Role = role,
                    Text = e.Append ? last.Text + e.Text : e.Text
                };
            }
            else
            {
                Messages.Add(new AgentMessageItem { Role = role, Text = e.Text });
            }
        });
        _agent.OnRunStateChanged += (_, e) => Ui(() =>
        {
            IsRunning = e.State == AgentRunState.Running;
            if (e.State is AgentRunState.Completed or AgentRunState.Failed or AgentRunState.Cancelled)
            {
                if (!string.IsNullOrWhiteSpace(e.Summary))
                    Messages.Add(new AgentMessageItem { Role = "status", Text = e.Summary });
            }
            StateChanged?.Invoke();
        });
    }

    public async Task SendAsync()
    {
        var task = InputText.Trim();
        if (string.IsNullOrWhiteSpace(task) || IsRunning) return;
        InputText = "";
        Messages.Add(new AgentMessageItem { Role = "user", Text = task });
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        StateChanged?.Invoke();
        await _agent.RunAsync(task, Strategy, DeepThinking, WebSearch, _runCts.Token);
    }

    public void Stop()
    {
        _runCts?.Cancel();
        _agent.Stop();
    }

    private static void Ui(Action action) =>
        App.MainWindow?.DispatcherQueue.TryEnqueue(() => action());
}
