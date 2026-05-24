using DeepSeek.Desktop.Services;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeepSeek.Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        EnableExternalApi.Checked += (_, _) => ExternalApiPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        EnableExternalApi.Unchecked += (_, _) => ExternalApiPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        Loaded += async (_, _) =>
        {
            LoadFields();
            await RefreshTuiStatusAsync();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (App.MainWindow is MainWindow mw)
            await mw.EnsureHostInitializedAsync();
    }

    private void LoadFields()
    {
        var cfg = ConfigStore.Load();
        Chat2ApiCompat.EnsureDefaultMappings(cfg);

        LoginStatusText.Text = string.IsNullOrWhiteSpace(cfg.WebUserToken)
            ? "未登录"
            : "已登录（网页会话已同步）";

        WorkspaceBox.Text = string.IsNullOrWhiteSpace(cfg.AgentWorkspaceRoot)
            ? AgentWorkspace.ResolveRoot(cfg)
            : cfg.AgentWorkspaceRoot;
        ModelBox.Text = cfg.Model;
        DeepThinkDefault.IsChecked = cfg.AgentDeepThinking;
        WebSearchDefault.IsChecked = cfg.AgentWebSearch;
        EnableExternalApi.IsChecked = cfg.EnableExternalOpenAiApi;
        EnableApiKeyAuth.IsChecked = cfg.EnableLocalApiKeyAuth;
        ExternalApiPanel.Visibility = cfg.EnableExternalOpenAiApi
            ? Visibility.Visible
            : Visibility.Collapsed;

        for (var i = 0; i < ApprovalBox.Items.Count; i++)
        {
            if (ApprovalBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), cfg.AgentApprovalMode, StringComparison.OrdinalIgnoreCase))
            {
                ApprovalBox.SelectedIndex = i;
                break;
            }
        }

        MappingSummaryText.Text = cfg.ModelMappings.Count == 0
            ? "无自定义映射（使用内置 DeepSeek 模型）"
            : $"已配置 {cfg.ModelMappings.Count} 条模型别名";

        var enabledKeys = cfg.LocalApiKeys.Count(k => k.Enabled);
        ApiKeySummaryText.Text = !cfg.EnableExternalOpenAiApi
            ? "未启用外部 API"
            : cfg.EnableLocalApiKeyAuth
                ? $"已启用鉴权，{enabledKeys} 个有效 Key"
                : "外部 API 已启用，未要求鉴权";

        var mcpEnabled = cfg.McpServers.Count(s => s.Enabled);
        McpSummaryText.Text = $"{mcpEnabled} / {cfg.McpServers.Count} 个 MCP 服务器已启用";
    }

    private Task RefreshTuiStatusAsync()
    {
        TuiStatusText.Text = "Agent Harness：进程内 C# 引擎（网页桥 + Chat2API）";
        return Task.CompletedTask;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ConfigStore.Load();
        cfg.AgentWorkspaceRoot = WorkspaceBox.Text.Trim();
        cfg.Model = ModelBox.Text.Trim();
        cfg.AgentDeepThinking = DeepThinkDefault.IsChecked == true;
        cfg.AgentWebSearch = WebSearchDefault.IsChecked == true;
        cfg.EnableExternalOpenAiApi = EnableExternalApi.IsChecked == true;
        cfg.EnableLocalApiKeyAuth = EnableApiKeyAuth.IsChecked == true;

        if (ApprovalBox.SelectedItem is ComboBoxItem approvalItem)
            cfg.AgentApprovalMode = approvalItem.Tag?.ToString() ?? "smart";

        Chat2ApiCompat.EnsureDefaultMappings(cfg);
        AppHost.Instance.SaveConfig(cfg);
        if (cfg.EnableExternalOpenAiApi)
            AppHost.Instance.ExternalApi?.EnsureExternalApiListening();
        AgentDesktopConfigSync.Apply(cfg);
        StatusText.Text = "已保存";
        LoadFields();
    }

    private void GenerateKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ConfigStore.Load();
        cfg.EnableLocalApiKeyAuth = true;
        var key = LocalApiKeyService.GenerateKeyValue();
        cfg.LocalApiKeys.Add(new LocalApiKey
        {
            Id = LocalApiKeyService.NewId(),
            Name = "WinUI 生成",
            Key = key,
            Enabled = true
        });
        AppHost.Instance.SaveConfig(cfg);
        StatusText.Text = "已生成 Key（请妥善保存，关闭后可在配置文件中查看）";
        LoadFields();
    }
}
