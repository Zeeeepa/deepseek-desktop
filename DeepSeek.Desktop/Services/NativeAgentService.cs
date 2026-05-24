using DeepSeek.Desktop.ViewModels;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.Harness;
using Microsoft.UI.Xaml.Controls;

namespace DeepSeek.Desktop.Services;

public sealed class NativeAgentService
{
    public event EventHandler<AgentMessageEventArgs>? OnMessage;
    public event EventHandler<AgentStreamDeltaEventArgs>? OnStreamDelta;
    public event EventHandler<AgentRunStateEventArgs>? OnRunStateChanged;

    private CancellationTokenSource? _cts;
    private DeepSeekHarnessRunner? _harness;

    public void Stop() => _cts?.Cancel();

    public async Task RunAsync(
        string task,
        string strategy,
        bool deepThink,
        bool webSearch,
        CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = _cts.Token;
        var host = AppHost.Instance;
        host.ReloadConfig();
        var config = host.Config;

        OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Running));
        OnMessage?.Invoke(this, new AgentMessageEventArgs("status", "Agent 运行中…", "started"));

        if (string.IsNullOrWhiteSpace(config.WebUserToken))
        {
            OnMessage?.Invoke(this, new AgentMessageEventArgs("status", "请先在「对话」页登录 DeepSeek。", "error"));
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Failed, "未登录"));
            return;
        }

        await host.EnsureStackLinkedAsync(ct);

        AgentModeHelper.ApplyAgentDefaults(config);
        config.AgentDeepThinking = deepThink;
        config.AgentWebSearch = webSearch;
        ConfigStore.Save(config);

        var webChat = new WinUiWebChatBridgeHostAdapter(host);
        _harness ??= new DeepSeekHarnessRunner(webChat, new McpHub(), RequestApprovalAsync);
        using var featureScope = Chat2ApiFeatureScope.Begin(deepThink, webSearch);
        host.BeginAgentLlmBridge();

        try
        {
            var result = await _harness.RunAsync(
                new HarnessRunRequest
                {
                    Config = config,
                    Prompt = task,
                    Strategy = strategy,
                    RefFileIds = Array.Empty<string>()
                },
                new HarnessRunCallbacks
                {
                    OnLog = line => OnMessage?.Invoke(this, new AgentMessageEventArgs("log", line, "log")),
                    OnAnswerDelta = (delta, append) =>
                    {
                        if (!string.IsNullOrEmpty(delta))
                            OnStreamDelta?.Invoke(this, new AgentStreamDeltaEventArgs(delta, append, isThinking: false));
                    },
                    OnThinking = (text, append) =>
                        OnStreamDelta?.Invoke(this, new AgentStreamDeltaEventArgs(text, append, isThinking: true)),
                    OnActivity = a => OnMessage?.Invoke(this,
                        new AgentMessageEventArgs("tool", $"{a.Verb} {a.Target}", a.Detail))
                },
                ct);

            OnRunStateChanged?.Invoke(this,
                new AgentRunStateEventArgs(AgentRunState.Completed, "完成", result.Answer));
        }
        catch (OperationCanceledException)
        {
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Cancelled, "已停止"));
        }
        catch (Exception ex)
        {
            OnRunStateChanged?.Invoke(this, new AgentRunStateEventArgs(AgentRunState.Failed, ex.Message));
        }
        finally
        {
            host.EndAgentLlmBridge();
        }
    }

    private Task<bool> RequestApprovalAsync(string toolName, string detail)
    {
        var tcs = new TaskCompletionSource<bool>();
        App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
        {
            var dlg = new ContentDialog
            {
                Title = "工具审批",
                Content = $"工具: {toolName}\n\n{detail}",
                PrimaryButtonText = "允许",
                CloseButtonText = "拒绝",
                XamlRoot = App.MainWindow.Content.XamlRoot
            };
            var r = await dlg.ShowAsync();
            tcs.TrySetResult(r == ContentDialogResult.Primary);
        });
        return tcs.Task;
    }
}
