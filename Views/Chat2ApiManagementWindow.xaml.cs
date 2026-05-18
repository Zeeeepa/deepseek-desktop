using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Views;

public partial class Chat2ApiManagementWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig>? _onSaved;
    private readonly ObservableCollection<ModelMappingEntry> _mappings = new();
    private readonly ObservableCollection<SessionRow> _sessions = new();
    private string _providerApiKey = "";

    public Chat2ApiManagementWindow(AppConfig config, Action<AppConfig>? onSaved = null)
    {
        InitializeComponent();
        _config = config;
        _onSaved = onSaved;
        MappingsGrid.ItemsSource = _mappings;
        _mappings.CollectionChanged += (_, _) => UpdateMappingsEmptyState();
        SessionsGrid.ItemsSource = _sessions;

        SessionModeCombo.Items.Add("单轮（single）");
        SessionModeCombo.Items.Add("多轮（multi）");
        SessionModeCombo.SelectedIndex =
            string.Equals(config.Chat2ApiSessionMode, "multi", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        ApiUrlText.Text = $"http://127.0.0.1:{config.LocalApiPort}/v1 · 客户端请使用 DeepSeek 模型名";
        ReloadMappings();
        UpdateMappingsEmptyState();
        RefreshProviderPanel();
        _ = RefreshSessionsAsync();
    }

    private void RefreshProviderPanel()
    {
        var snap = Chat2ApiProviderService.Build(_config);
        _providerApiKey = snap.ApiKeyForClients;
        ProviderStatsText.Text =
            $"总计: 1 · {(snap.Online ? 1 : 0)} 在线 · {(snap.Enabled ? 1 : 0)} 已启用 · 1 内置, 0 自定义";
        ProviderOnlineDot.Fill = new SolidColorBrush(snap.Online
            ? Color.FromRgb(0x22, 0xC5, 0x5E)
            : Color.FromRgb(0xEF, 0x44, 0x44));
        ProviderOnlineText.Text = snap.Online ? "在线" : "离线";
        ProviderAccountText.Text = snap.Online ? "1 / 1 在线" : "0 / 1 在线";
        ProviderModelCountText.Text = snap.ModelCount.ToString();
        ProviderAuthText.Text = snap.AuthType == "user_token" ? "User Token" : snap.AuthType;
        ProviderBaseUrlBox.Text = snap.Chat2ApiBaseUrl;
        ProviderApiKeyBox.Password = snap.ApiKeyForClients;
        ProviderTuiUrlBox.Text = snap.TuiRuntimeUrl;
        ProviderIntegrationHint.Text =
            $"已写入 {snap.TuiConfigPath}（DEEPSEEK_BASE_URL / DEEPSEEK_API_KEY）\n集成文件: {snap.IntegrationFilePath}";
    }

    private void RefreshProvider_Click(object sender, RoutedEventArgs e) => RefreshProviderPanel();

    private void CopyBaseUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ProviderBaseUrlBox.Text))
            Clipboard.SetText(ProviderBaseUrlBox.Text);
        DsMessageDialog.Info(this, "Base URL 已复制到剪贴板。", "供应商");
    }

    private void CopyApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_providerApiKey))
            Clipboard.SetText(_providerApiKey);
        DsMessageDialog.Info(this, "API Key 已复制到剪贴板。", "供应商");
    }

    private void CopyTuiUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ProviderTuiUrlBox.Text))
            Clipboard.SetText(ProviderTuiUrlBox.Text);
        DsMessageDialog.Info(this, "TUI 运行时地址已复制。", "供应商");
    }

    private void OpenIntegrationFile_Click(object sender, RoutedEventArgs e)
    {
        Chat2ApiProviderService.WriteIntegrationFile(_config);
        var path = Chat2ApiProviderService.IntegrationFilePath;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        DsMessageDialog.Warning(this, "集成文件尚未生成，请先登录网页。", "供应商");
    }

    private void OpenTuiDocs_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://deepseek-tui.com/zh");

    private void UpdateMappingsEmptyState()
    {
        var empty = _mappings.Count == 0;
        MappingsEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        MappingsGrid.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ReloadMappings()
    {
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        _mappings.Clear();
        foreach (var m in _config.ModelMappings)
            _mappings.Add(new ModelMappingEntry { RequestModel = m.RequestModel, ActualModel = m.ActualModel });
        UpdateMappingsEmptyState();
    }

    private async Task RefreshSessionsAsync()
    {
        _sessions.Clear();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var url = $"http://127.0.0.1:{_config.LocalApiPort}/v1/admin/sessions";
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var mode = doc.RootElement.TryGetProperty("mode", out var m) ? m.GetString() : "single";
            SessionHintText.Text = $"当前模式: {mode} · 多轮请求需带 session_id";

            if (!doc.RootElement.TryGetProperty("sessions", out var arr)) return;
            foreach (var el in arr.EnumerateArray())
            {
                var last = el.TryGetProperty("lastUsedAt", out var lu) ? lu.GetInt64() : 0;
                _sessions.Add(new SessionRow
                {
                    ClientSessionId = el.GetProperty("clientSessionId").GetString() ?? "",
                    WebSessionId = el.GetProperty("webSessionId").GetString() ?? "",
                    MessageCount = el.TryGetProperty("messageCount", out var mc) ? mc.GetInt32() : 0,
                    LastUsedText = last > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(last).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        : "—"
                });
            }
        }
        catch
        {
            SessionHintText.Text = "无法读取会话列表（请确认 DeepSeek 已启动且本地 API 在运行）";
        }
    }

    private void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        _mappings.Add(new ModelMappingEntry
        {
            RequestModel = "deepseek-v4-pro",
            ActualModel = Chat2ApiCompat.DefaultModel
        });
    }

    private void DeleteMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ModelMappingEntry row })
            _mappings.Remove(row);
    }

    private void ResetMappings_Click(object sender, RoutedEventArgs e)
    {
        if (!DsMessageDialog.Confirm(this, "恢复 DeepSeek 默认模型别名？", "确认", "确定", "取消"))
            return;
        _config.ModelMappings.Clear();
        Chat2ApiCompat.EnsureDefaultMappings(_config);
        ReloadMappings();
    }

    private async void RefreshSessions_Click(object sender, RoutedEventArgs e) => await RefreshSessionsAsync();

    private async void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sid } || string.IsNullOrWhiteSpace(sid)) return;
        try
        {
            using var http = new HttpClient();
            await http.DeleteAsync($"http://127.0.0.1:{_config.LocalApiPort}/v1/admin/sessions/{Uri.EscapeDataString(sid)}");
            await RefreshSessionsAsync();
        }
        catch (Exception ex)
        {
            DsMessageDialog.Warning(this, ex.Message, "删除会话");
        }
    }

    private async void ClearSessions_Click(object sender, RoutedEventArgs e)
    {
        if (!DsMessageDialog.Confirm(this, "清空所有已缓存的 session_id 映射？", "确认", "确定", "取消"))
            return;

        foreach (var row in _sessions.ToList())
        {
            try
            {
                using var http = new HttpClient();
                await http.DeleteAsync(
                    $"http://127.0.0.1:{_config.LocalApiPort}/v1/admin/sessions/{Uri.EscapeDataString(row.ClientSessionId)}");
            }
            catch { /* ignore */ }
        }

        await RefreshSessionsAsync();
    }

    private void OpenApiKeys_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ApiKeyManagementWindow(_config, c =>
        {
            _config.EnableLocalApiKeyAuth = c.EnableLocalApiKeyAuth;
            _config.LocalApiKeys = c.LocalApiKeys;
        })
        { Owner = this };
        dlg.ShowDialog();
    }

    private void OpenDocs_Click(object sender, RoutedEventArgs e) =>
        OpenUrl(Chat2ApiCompat.OfficialApiDocsUrl);

    private void OpenOfficialDocs_Click(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        OpenUrl(e.Uri?.AbsoluteUri ?? Chat2ApiCompat.OfficialApiDocsUrl);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Clipboard.SetText(url);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.ModelMappings = _mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.RequestModel))
            .Select(m => new ModelMappingEntry
            {
                RequestModel = m.RequestModel.Trim(),
                ActualModel = string.IsNullOrWhiteSpace(m.ActualModel) ? m.RequestModel.Trim() : m.ActualModel.Trim()
            })
            .ToList();
        _config.Chat2ApiSessionMode = SessionModeCombo.SelectedIndex == 1 ? "multi" : "single";
        ConfigStore.Save(_config);
        _onSaved?.Invoke(_config);
        DsMessageDialog.Info(this, "已保存。", "本地 API");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class SessionRow
    {
        public string ClientSessionId { get; init; } = "";
        public string WebSessionId { get; init; } = "";
        public int MessageCount { get; init; }
        public string LastUsedText { get; init; } = "";
    }
}
