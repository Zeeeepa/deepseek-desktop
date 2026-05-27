using System.Text.Json;
using System.Windows;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser;

public partial class MainWindow
{
    private void ScheduleWorkModeSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                await Dispatcher.InvokeAsync(async () =>
                {
                    if (_agentHost is null || _webHost is null) return;
                    WorkModeTrace.Write("SelfTest: ApplyWorkMode agent");
                    await _agentHost.VerifyWorkModeSwitchAsync("agent");
                    await Task.Delay(1200);
                    WorkModeTrace.Write($"SelfTest: after agent IsAgentVisible={_webHost.IsAgentVisible}");

                    await _agentHost.VerifyWorkModeSwitchAsync("chat");
                    await Task.Delay(1200);
                    WorkModeTrace.Write($"SelfTest: after chat IsAgentVisible={_webHost.IsAgentVisible}");

                    await _webHost.Chat.EnsureChatModeFloaterAsync();
                    for (var probeAttempt = 0; probeAttempt < 8; probeAttempt++)
                    {
                        await Task.Delay(probeAttempt == 0 ? 600 : 400);
                        await _webHost.Chat.EnsureChatModeFloaterAsync();
                        var floaterRaw = await _webHost.Chat.EvaluateOnPageAsync(ChatModeFloaterScript.Probe);
                        if (TryParseFloaterProbe(floaterRaw, out var probeOk, out var probeDetail) && probeOk)
                        {
                            Environment.ExitCode = 0;
                            WorkModeTrace.Write("SelfTest: floater PASS " + probeDetail);
                            break;
                        }

                        if (probeAttempt == 7)
                        {
                            WorkModeTrace.Write(TryParseFloaterProbe(floaterRaw, out _, out var failDetail)
                                ? "SelfTest: floater FAIL " + failDetail
                                : "SelfTest: floater FAIL parse error raw=" + TrimForLog(floaterRaw));
                            Environment.ExitCode = 1;
                        }
                    }

                    var core = WebView.CoreWebView2;
                    if (core is not null)
                    {
                        await core.ExecuteScriptAsync(
                            "(function(){try{"
                            + "if(window.DsWorkMode&&window.DsWorkMode.requestToggle){window.DsWorkMode.requestToggle({});return;}"
                            + "if(window.chrome&&window.chrome.webview){"
                            + "window.chrome.webview.postMessage(JSON.stringify({type:'toggleWorkMode'}));}"
                            + "}catch(e){console.warn(e);}})();");
                        await Task.Delay(1500);
                        WorkModeTrace.Write($"SelfTest: after JS toggle IsAgentVisible={_webHost.IsAgentVisible}");
                    }

                    if (DeepSeekDesktopApp.IsEnvEnabled(
                            DeepSeekDesktopApp.VerifySmoothnessEnvVar,
                            DeepSeekDesktopApp.VerifySmoothnessEnvVar))
                    {
                        var smoothOk = VerifySmoothnessCounters();
                        if (!smoothOk && Environment.ExitCode == 0)
                            Environment.ExitCode = 1;
                    }

                    if (DeepSeekDesktopApp.IsEnvEnabled(
                            DeepSeekDesktopApp.VerifyWorkModeUiEnvVar,
                            DeepSeekDesktopApp.VerifyWorkModeUiEnvVar)
                        || DeepSeekDesktopApp.IsEnvEnabled(
                            DeepSeekDesktopApp.VerifySmoothnessUiEnvVar,
                            DeepSeekDesktopApp.VerifySmoothnessUiEnvVar))
                    {
                        WorkModeTrace.Write("WorkModeUiVerify: exiting");
                        ExitApplication();
                    }
                });
                return;
            }

            WorkModeTrace.Write("SelfTest: timeout waiting for web ready");
            Environment.ExitCode = 1;
        });
    }

    private static bool VerifySmoothnessCounters()
    {
        var burst = DesktopUiTrace.InjectBurstCount;
        var loading = DesktopUiTrace.LoadingOverlayShowCount;
        WorkModeTrace.Write(
            $"SmoothnessSelfTest: injectBursts={burst} loadingShows={loading} spaRoutes={DesktopUiTrace.SpaRouteCount}");

        var ok = true;
        if (burst > 12)
        {
            WorkModeTrace.Write($"SmoothnessSelfTest: FAIL too many inject bursts ({burst})");
            ok = false;
        }

        if (loading > 4)
        {
            WorkModeTrace.Write($"SmoothnessSelfTest: FAIL too many loading overlays ({loading})");
            ok = false;
        }

        if (ok)
            WorkModeTrace.Write("SmoothnessSelfTest: PASS");

        return ok;
    }

    private static string TrimForLog(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return "(empty)";
        var s = raw.Trim().Trim('"');
        return s.Length > 200 ? s[..200] + "…" : s;
    }

    private static bool TryParseFloaterProbe(string? raw, out bool ok, out string detail)
    {
        ok = false;
        detail = TrimForLog(raw);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return false;
        try
        {
            var json = raw.Trim();
            if (json.StartsWith('"') && json.EndsWith('"'))
                json = JsonSerializer.Deserialize<string>(json) ?? json;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            detail = root.GetRawText();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ScheduleAgentHelloSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null || _webHost is null) return;
                        WorkModeTrace.Write("AgentSelfTest: web ready, warming bridge");
                        await _agentHost.WarmDsdApiBridgeAsync();
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        _agentHost.ReloadConfig();
                        var cfg = ConfigStore.Load();
                        if (string.IsNullOrWhiteSpace(cfg.WebUserToken))
                        {
                            WorkModeTrace.Write("AgentSelfTest: FAIL no webUserToken (login required)");
                            Environment.ExitCode = 2;
                            System.Windows.Application.Current.Shutdown();
                            return;
                        }

                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                        await _agentHost.VerifyAgentHelloAsync(cts.Token);
                        Environment.ExitCode = 0;
                        System.Windows.Application.Current.Shutdown();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("AgentSelfTest: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("AgentSelfTest: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
        });
    }

    private void ScheduleAgentTaskSelfTest()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 120; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _webHost is { AgentPageReady: true };
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null || _webHost is null) return;
                        WorkModeTrace.Write("AgentTaskTest: web ready, warming bridge");
                        await _agentHost.WarmDsdApiBridgeAsync();
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        _agentHost.ReloadConfig();
                        var cfg = ConfigStore.Load();
                        if (string.IsNullOrWhiteSpace(cfg.WebUserToken))
                        {
                            WorkModeTrace.Write("AgentTaskTest: FAIL no webUserToken (login required)");
                            Environment.ExitCode = 2;
                            System.Windows.Application.Current.Shutdown();
                            return;
                        }

                        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                        await _agentHost.VerifyAgentTaskAsync(cts.Token);
                        Environment.ExitCode = 0;
                        System.Windows.Application.Current.Shutdown();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("AgentTaskTest: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("AgentTaskTest: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
        });
    }

    private void ScheduleShutdownVerifyExit()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 90; attempt++)
            {
                await Task.Delay(1000);
                var ready = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ready = _webViewReady && _agentHost is not null;
                });
                if (!ready) continue;

                try
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        if (_agentHost is null) return;
                        WorkModeTrace.Write("ShutdownVerify: warming embedded stack");
                        await _agentHost.EnsureEmbeddedStackLinkedAsync();
                        WorkModeTrace.Write("ShutdownVerify: exiting gracefully");
                        ExitApplication();
                    });
                }
                catch (Exception ex)
                {
                    WorkModeTrace.Write("ShutdownVerify: FAIL " + ex.Message);
                    Environment.ExitCode = 1;
                    await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
                }

                return;
            }

            WorkModeTrace.Write("ShutdownVerify: timeout waiting for web ready");
            Environment.ExitCode = 3;
            await Dispatcher.InvokeAsync(() => System.Windows.Application.Current.Shutdown());
        });
    }
}
