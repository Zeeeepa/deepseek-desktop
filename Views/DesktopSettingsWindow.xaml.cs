using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using DeepSeekBrowser.Services.DeepSeekTui;

namespace DeepSeekBrowser.Views;

public partial class DesktopSettingsWindow : System.Windows.Window
{
    private readonly McpHub _mcpHub;
    private ObservableCollection<McpServerConfig> _mcpItems;

    public AppConfig? Config { get; private set; }

    public DesktopSettingsWindow(AppConfig config, McpHub mcpHub)
    {
        InitializeComponent();
        _mcpHub = mcpHub;
        Config = config;
        _mcpItems = new ObservableCollection<McpServerConfig>(config.McpServers);
        PortBox.Text = config.LocalApiPort.ToString();
        MaxStepsBox.Text = config.MaxAgentSteps.ToString();
        MaxSubStepsBox.Text = config.MaxSubAgentSteps.ToString();
        AgentStrategyCombo.Items.Clear();
        AgentStrategyCombo.Items.Add("Agent 模式（多步工具）");
        AgentStrategyCombo.Items.Add("Plan 模式（只读调研）");
        AgentStrategyCombo.SelectedIndex = string.Equals(config.DefaultAgentStrategy, AgentStrategies.Plan,
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        LocalApiUrlText.Text = $"http://127.0.0.1:{config.LocalApiPort}/v1";
        ConfigPathText.Text = ConfigStore.ConfigFilePath;
        WebTokenStatus.Text = string.IsNullOrWhiteSpace(config.WebUserToken) ? "未登录" : "已登录";
        WebTokenStatus.Foreground = string.IsNullOrWhiteSpace(config.WebUserToken)
            ? (Brush)FindResource("DsDanger")
            : (Brush)FindResource("DsSuccess");
        BindAgentStorageSettings(config);
        BindDeepSeekTuiSettings(config);
        Loaded += async (_, _) => await RefreshDeepSeekTuiStatusAsync(config);
        BindChat2ApiSummary(config);
        RefreshMcpList();
    }

    private void BindChat2ApiSummary(AppConfig config)
    {
        Chat2ApiCompat.EnsureDefaultMappings(config);
        var maps = config.ModelMappings.Count;
        var keys = config.LocalApiKeys.Count;
        var mode = string.Equals(config.Chat2ApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase)
            ? "多轮" : "单轮";
        Chat2ApiSummaryText.Text =
            $"客户端模型: {Chat2ApiCompat.DefaultModel} · 会话 {mode} · {maps} 条别名 · API Key {keys} 个" +
            (config.EnableLocalApiKeyAuth ? "（认证已启用）" : "");
    }

    private void ManageChat2Api_Click(object sender, RoutedEventArgs e)
    {
        if (Config is null) Config = new AppConfig();
        var dlg = new Chat2ApiManagementWindow(Config, c =>
        {
            Config = c;
            BindChat2ApiSummary(c);
        })
        { Owner = this };
        dlg.ShowDialog();
    }

    private void BindDeepSeekTuiSettings(AppConfig config)
    {
        var port = config.DeepSeekTuiRuntimePort > 0 ? config.DeepSeekTuiRuntimePort : 7878;
        var bundled = DeepSeekTuiBundle.IsBundledComplete ? "已内置" : "未内置（将自动下载）";
        var sourceRoot = DeepSeekTuiSourceBuild.ResolveRepositoryRoot(config);
        var sourceHint = sourceRoot is null
            ? "未检测到本地源码"
            : (DeepSeekTuiSourceBuild.TryResolveReleaseBinaries(config) is not null
                ? "已编译 release"
                : "源码已找到，未编译");
        TuiPortInfoText.Text =
            "DeepSeek-TUI Agent 引擎（内置）· Plan / Agent / YOLO\n" +
            $"Runtime API：127.0.0.1:{port} · 二进制：{bundled} · 源码：{sourceHint}\n" +
            "LLM：Chat2API（网页 Token）→ ~/.deepseek/config.toml · 非 api.deepseek.com 直连\n" +
            "模式：/react → Agent · /plan → Plan";
        TuiSourcePathBox.Text = config.DeepSeekTuiSourcePath ?? "";
        AgentWorkspaceBox.Text = config.AgentWorkspaceRoot ?? "";
        AgentAllowShellCheck.IsChecked = config.AgentAllowShell;
        AdaptiveOutputCheck.IsChecked = config.EnableAdaptiveOutputEscalation;
        AgentApprovalCombo.Items.Clear();
        AgentApprovalCombo.Items.Add("智能（只读自动）");
        AgentApprovalCombo.Items.Add("只读自动");
        AgentApprovalCombo.Items.Add("全部需确认");
        AgentApprovalCombo.Items.Add("从不确认（不安全）");
        AgentApprovalCombo.SelectedIndex = config.AgentApprovalMode?.ToLowerInvariant() switch
        {
            "readonly" => 1,
            "always" => 2,
            "never" => 3,
            _ => 0
        };
    }

    private void BindAgentStorageSettings(AppConfig config)
    {
        var store = new AgentSessionStore();
        var (bytes, count) = store.GetStats();
        AgentStorageStatsText.Text =
            $"当前 {count} 条对话，约 {FormatBytes(bytes)}。目录：{store.StorageDirectory}";

        AgentRetentionCombo.Items.Clear();
        AgentRetentionCombo.Items.Add("不自动删除");
        AgentRetentionCombo.Items.Add("7 天");
        AgentRetentionCombo.Items.Add("30 天");
        AgentRetentionCombo.Items.Add("90 天");
        AgentRetentionCombo.Items.Add("180 天");
        AgentRetentionCombo.SelectedIndex = config.AgentSessionRetentionDays switch
        {
            0 => 0,
            7 => 1,
            30 => 2,
            90 => 3,
            180 => 4,
            _ => 2
        };
        AgentMaxGbBox.Text = config.AgentSessionMaxStorageGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        AgentAutoCleanupCheck.IsChecked = config.AgentSessionAutoCleanup;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("0.##") + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("0.##") + " GB";
    }

    private async void AgentCleanupNow_Click(object sender, RoutedEventArgs e)
    {
        if (Config is null) return;
        var store = new AgentSessionStore();
        var deleted = store.ApplyRetentionPolicy(Config);
        BindAgentStorageSettings(Config);
        DsMessageDialog.Info(
            this,
            deleted.Count > 0
                ? $"已按规则清理 {deleted.Count} 条最旧或超期的对话。"
                : "无需清理，或未启用保留/容量规则。",
            "Agent 存储");
        await Task.CompletedTask;
    }

    private void OpenAgentStorage_Click(object sender, RoutedEventArgs e)
    {
        var dir = new AgentSessionStore().StorageDirectory;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });
    }

    private void RefreshMcpList()
    {
        McpList.Children.Clear();

        if (_mcpItems.Count == 0)
        {
            McpList.Children.Add(new TextBlock
            {
                Text = "尚未添加 MCP 服务器。点击列表下方「添加 MCP 服务器」，连接成功后 Agent 可动态发现并调用工具。",
                Style = (Style)FindResource("DsHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        foreach (var server in _mcpItems)
            McpList.Children.Add(BuildServerCard(server));
    }

    private Border BuildServerCard(McpServerConfig server)
    {
        var rowStyle = (Style)FindResource("DsMcpRow");
        var bodyStyle = (Style)FindResource("DsBody");
        var captionBrush = (Brush)FindResource("DsCaptionBrush");
        var success = (Brush)FindResource("DsSuccess");
        var muted = (Brush)FindResource("DsCaptionBrush");
        var ghostStyle = (Style)FindResource("DsGhostButton");
        var switchStyle = (Style)FindResource("DsSwitch");

        var connected = server.Enabled && _mcpHub.IsConnected(server.Id);
        var toolCount = _mcpHub.GetToolCount(server.Id);

        var card = new Border { Style = rowStyle, Margin = new Thickness(0, 0, 0, 10) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = connected ? success : muted,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 10, 0)
        };
        Grid.SetRow(dot, 0);
        Grid.SetColumn(dot, 0);

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = server.Name,
            Style = bodyStyle,
            FontWeight = FontWeights.SemiBold
        });

        var transport = server.IsRemote ? "HTTP/SSE（远程）" : "stdio（本机）";
        info.Children.Add(new TextBlock
        {
            Text = $"{transport} · {Truncate(server.DisplayEndpoint, 40)}",
            FontSize = 11,
            Foreground = captionBrush,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var status = new TextBlock { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        if (!server.Enabled)
        {
            status.Text = "已禁用";
            status.Foreground = captionBrush;
        }
        else if (connected)
        {
            status.Text = $"已连接 · {toolCount} 个工具可用（动态能力发现）";
            status.Foreground = success;
        }
        else
        {
            status.Text = "未连接 — 请打开开关或点击「连接全部」";
            status.Foreground = captionBrush;
        }

        info.Children.Add(status);
        Grid.SetRow(info, 0);
        Grid.SetColumn(info, 1);

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(18, 10, 0, 0)
        };
        Grid.SetRow(actions, 1);
        Grid.SetColumn(actions, 0);
        Grid.SetColumnSpan(actions, 2);

        var toolsBtn = new Button
        {
            Content = "工具",
            Style = ghostStyle,
            MinWidth = 48,
            MinHeight = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = connected && toolCount > 0
        };
        toolsBtn.Click += async (_, _) => await ShowToolsAsync(server);

        var toggle = new ToggleButton
        {
            Style = switchStyle,
            IsChecked = server.Enabled,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "启用后作为 MCP Client 连接此 Server"
        };
        toggle.Click += async (_, _) => await OnServerToggleAsync(server, toggle);

        var edit = new Button
        {
            Content = "编辑",
            Style = ghostStyle,
            MinWidth = 48,
            MinHeight = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 6, 0)
        };
        edit.Click += (_, _) => EditServer(server);

        var remove = new Button
        {
            Content = "删除",
            Style = ghostStyle,
            MinWidth = 48,
            MinHeight = 28,
            Padding = new Thickness(8, 0, 8, 0),
            Foreground = (Brush)FindResource("DsDanger")
        };
        remove.Click += async (_, _) => await RemoveServerAsync(server);

        actions.Children.Add(toolsBtn);
        actions.Children.Add(toggle);
        actions.Children.Add(edit);
        actions.Children.Add(remove);

        grid.Children.Add(dot);
        grid.Children.Add(info);
        grid.Children.Add(actions);
        card.Child = grid;
        return card;
    }

    private async Task ShowToolsAsync(McpServerConfig server)
    {
        try
        {
            var tools = await _mcpHub.ListToolNamesAsync(server.Id, CancellationToken.None);
            if (tools.Count == 0)
            {
                DsMessageDialog.Info(this, "当前没有可用工具。", server.Name);
                return;
            }

            new McpToolsWindow(server.Name, tools) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            DsMessageDialog.Warning(this, ex.Message, "工具列表");
        }
    }

    private async Task OnServerToggleAsync(McpServerConfig server, ToggleButton toggle)
    {
        server.Enabled = toggle.IsChecked == true;
        try
        {
            if (server.Enabled)
            {
                if (!_mcpHub.IsConnected(server.Id))
                    await _mcpHub.ConnectAsync(server, _ => { }, CancellationToken.None);
            }
            else
                await _mcpHub.DisconnectAsync(server.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            DsMessageDialog.Warning(this, ex.Message, server.Name);
            server.Enabled = false;
            toggle.IsChecked = false;
        }

        RefreshMcpList();
    }

    private void EditServer(McpServerConfig server)
    {
        var dlg = new McpServerEditorWindow(server) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            var idx = _mcpItems.IndexOf(server);
            if (idx >= 0) _mcpItems[idx] = dlg.Result;
            RefreshMcpList();
        }
    }

    private async Task RemoveServerAsync(McpServerConfig server)
    {
        if (!DsMessageDialog.Confirm(this, $"确定删除 MCP 服务器「{server.Name}」？", "删除", "删除", "取消"))
            return;

        try { await _mcpHub.DisconnectAsync(server.Id, CancellationToken.None); }
        catch { /* ignore */ }

        _mcpItems.Remove(server);
        RefreshMcpList();
    }

    private void AddMcp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new McpServerEditorWindow { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is not null)
        {
            _mcpItems.Add(dlg.Result);
            RefreshMcpList();
        }
    }

    private void CopyConfigPath_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(ConfigStore.ConfigFilePath);

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDirectory);
        if (!File.Exists(ConfigStore.ConfigFilePath))
            ConfigStore.Save(Config ?? new AppConfig { McpServers = _mcpItems.ToList() });

        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigStore.ConfigFilePath,
            UseShellExecute = true
        });
    }

    private async void ConnectAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _mcpHub.DisconnectAllAsync(CancellationToken.None);
            var errors = await _mcpHub.ConnectEnabledAsync(_mcpItems, _ => { }, CancellationToken.None);
            var totalTools = _mcpItems.Where(s => s.Enabled).Sum(s => _mcpHub.GetToolCount(s.Id));

            if (Config is not null)
            {
                Config.McpServers = _mcpItems.ToList();
                ConfigStore.Save(Config);
            }

            RefreshMcpList();
            var summary = $"已连接 {_mcpHub.ConnectedCount} 个 MCP Server，共发现 {totalTools} 个工具。";
            if (errors.Count > 0)
                summary += "\n\n失败:\n" + string.Join("\n", errors);

            if (errors.Count > 0)
                DsMessageDialog.Warning(this, summary, "MCP");
            else
                DsMessageDialog.Info(this, summary, "MCP");
        }
        catch (Exception ex)
        {
            RefreshMcpList();
            DsMessageDialog.Warning(this, ex.Message, "MCP");
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Config is null) Config = new AppConfig();
        if (int.TryParse(PortBox.Text, out var port))
            Config.LocalApiPort = Math.Clamp(port, 1024, 65535);
        if (int.TryParse(MaxStepsBox.Text, out var steps))
            Config.MaxAgentSteps = Math.Clamp(steps, 1, 100);
        if (int.TryParse(MaxSubStepsBox.Text, out var subSteps))
            Config.MaxSubAgentSteps = Math.Clamp(subSteps, 1, 50);
        Config.DefaultAgentStrategy = AgentStrategyCombo.SelectedIndex == 1
            ? AgentStrategies.Plan
            : AgentStrategies.React;
        Config.AgentSessionRetentionDays = AgentRetentionCombo.SelectedIndex switch
        {
            1 => 7,
            2 => 30,
            3 => 90,
            4 => 180,
            _ => 0
        };
        if (double.TryParse(AgentMaxGbBox.Text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var maxGb))
            Config.AgentSessionMaxStorageGb = Math.Max(0, maxGb);
        Config.AgentSessionAutoCleanup = AgentAutoCleanupCheck.IsChecked == true;
        Config.DeepSeekTuiSourcePath = TuiSourcePathBox.Text.Trim();
        Config.AgentWorkspaceRoot = AgentWorkspaceBox.Text.Trim();
        Config.AgentAllowShell = AgentAllowShellCheck.IsChecked == true;
        Config.EnableAdaptiveOutputEscalation = AdaptiveOutputCheck.IsChecked == true;
        Config.AgentApprovalMode = AgentApprovalCombo.SelectedIndex switch
        {
            1 => "readonly",
            2 => "always",
            3 => "never",
            _ => "smart"
        };
        Config.AgentAutoApproveReadOnly = Config.AgentApprovalMode is "smart" or "readonly";
        Config.McpServers = _mcpItems.ToList();
        DeepSeekTuiConfigSync.Apply(Config);
        ConfigStore.Save(Config);
        DialogResult = true;
        Close();
    }

    private async Task RefreshDeepSeekTuiStatusAsync(AppConfig config)
    {
        try
        {
            await DeepSeekTuiBundle.EnsureBinariesAsync(config);
            var ver = await DeepSeekTuiBundle.TryGetVersionAsync(config.DeepSeekTuiExecutablePath);
            if (!string.IsNullOrWhiteSpace(ver))
                TuiPortInfoText.Text += $"\n版本：{ver.Trim()}";
            using var doc = await DeepSeekTuiBundle.TryDoctorJsonAsync(config.DeepSeekTuiExecutablePath);
            if (doc is not null &&
                doc.RootElement.TryGetProperty("api_key", out var key) &&
                key.TryGetProperty("source", out var src))
            {
                TuiPortInfoText.Text += $"\ndoctor：api_key={src.GetString()}";
            }
        }
        catch (Exception ex)
        {
            TuiPortInfoText.Text += "\n状态检测：" + ex.Message;
        }
    }

    private void OpenDeepSeekHome_Click(object sender, RoutedEventArgs e)
    {
        var dir = DeepSeekTuiConfigSync.HomeDirectory;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private async void RunDeepSeekDoctor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DeepSeekTuiBundle.EnsureBinariesAsync(Config);
            if (Config is not null)
                DeepSeekTuiConfigSync.Apply(Config);
            using var doc = await DeepSeekTuiBundle.TryDoctorJsonAsync(Config?.DeepSeekTuiExecutablePath);
            var text = doc is null ? "doctor 无输出，请确认已登录网页或配置 API。" : doc.RootElement.GetRawText();
            DsMessageDialog.Info(this, text, "deepseek doctor --json");
        }
        catch (Exception ex)
        {
            DsMessageDialog.Warning(this, ex.Message, "doctor");
        }
    }

    private async void BuildDeepSeekTuiFromSource_Click(object sender, RoutedEventArgs e)
    {
        if (Config is null) Config = new AppConfig();
        Config.DeepSeekTuiSourcePath = TuiSourcePathBox.Text.Trim();
        var repo = DeepSeekTuiSourceBuild.ResolveRepositoryRoot(Config);
        if (repo is null)
        {
            DsMessageDialog.Warning(this, "请填写有效的 DeepSeek-TUI 源码根目录（含 Cargo.toml）。", "编译 TUI");
            return;
        }

        try
        {
            await DeepSeekTuiSourceBuild.BuildReleaseAsync(repo);
            await DeepSeekTuiSourceBuild.TryCopyReleaseToToolsAsync(Config);
            await RefreshDeepSeekTuiStatusAsync(Config);
            DsMessageDialog.Info(this, "已编译并复制到 Assets/tools。\n请重新运行 build.ps1 部署桌面端，或重启应用。", "编译 TUI");
        }
        catch (Exception ex)
        {
            DsMessageDialog.Warning(this, ex.Message, "编译 TUI");
        }
    }

    private void OpenDeepSeekDocs_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://deepseek-tui.com/zh/docs",
            UseShellExecute = true
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "—";
        return text.Length <= max ? text : text[..max] + "…";
    }
}
