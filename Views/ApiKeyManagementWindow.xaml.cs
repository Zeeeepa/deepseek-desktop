using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;
namespace DeepSeekBrowser.Views;

public partial class ApiKeyManagementWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action<AppConfig>? _onConfigSaved;
    private readonly ObservableCollection<ApiKeyRowViewModel> _rows = new();

    public ApiKeyManagementWindow(AppConfig config, Action<AppConfig>? onConfigSaved = null)
    {
        InitializeComponent();
        _config = config;
        _onConfigSaved = onConfigSaved;
        KeysGrid.ItemsSource = _rows;
        ReloadUi();
    }

    private void ReloadUi()
    {
        ServerStatusText.Text = $"http://127.0.0.1:{_config.LocalApiPort}/v1";
        EnableAuthCheck.IsChecked = _config.EnableLocalApiKeyAuth;
        UpdateAuthStatus();
        ReloadKeys();
    }

    private void ReloadKeys()
    {
        _rows.Clear();
        foreach (var k in _config.LocalApiKeys.OrderByDescending(x => x.CreatedAt))
            _rows.Add(ApiKeyRowViewModel.From(k));

        var count = _config.LocalApiKeys.Count;
        var enabled = _config.LocalApiKeys.Count(k => k.Enabled);
        KeyStatsText.Text = $"共 {count} 个 API Key，{enabled} 个已启用";
        EmptyPanel.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeysGrid.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Persist()
    {
        ConfigStore.Save(_config);
        _onConfigSaved?.Invoke(_config);
        UpdateAuthStatus();
        ReloadKeys();
    }

    private void UpdateAuthStatus()
    {
        AuthStatusText.Text = _config.EnableLocalApiKeyAuth
            ? (LocalApiKeyService.ShouldEnforceAuth(_config)
                ? "状态：已启用（外部请求须携带 Key）"
                : "状态：已启用，但尚无可用 Key")
            : "状态：已禁用";
    }

    private void EnableAuth_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _config.EnableLocalApiKeyAuth = EnableAuthCheck.IsChecked == true;
        Persist();
    }

    private void CreateKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ApiKeyCreateDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.KeyName))
            return;

        var entity = new LocalApiKey
        {
            Id = LocalApiKeyService.NewId(),
            Name = dlg.KeyName.Trim(),
            Key = LocalApiKeyService.GenerateKeyValue(),
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UsageCount = 0
        };
        _config.LocalApiKeys.Add(entity);
        Persist();

        DsMessageDialog.Info(
            this,
            $"已创建 API Key：{entity.Name}\n\n{entity.Key}\n\n请立即复制保存，关闭后可在列表中查看掩码。",
            "API Key 已创建");
        Clipboard.SetText(entity.Key);
    }

    private void CopyKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key } || string.IsNullOrWhiteSpace(key))
            return;
        Clipboard.SetText(key);
        DsMessageDialog.Info(this, "已复制到剪贴板。", "复制");
    }

    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;
        var key = _config.LocalApiKeys.FirstOrDefault(k => k.Id == id);
        if (key is null) return;
        if (!DsMessageDialog.Confirm(this, $"确定删除「{key.Name}」？", "删除 API Key", "删除", "取消"))
            return;

        _config.LocalApiKeys.RemoveAll(k => k.Id == id);
        Persist();
    }

    private void KeyEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ApiKeyRowViewModel row })
            return;
        SetKeyEnabled(row.Id, row.Enabled);
    }

    private void SetKeyEnabled(string id, bool enabled)
    {
        var key = _config.LocalApiKeys.FirstOrDefault(k => k.Id == id);
        if (key is null || key.Enabled == enabled) return;
        key.Enabled = enabled;
        Persist();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class ApiKeyRowViewModel : INotifyPropertyChanged
    {
        private bool _enabled;

        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Key { get; init; } = "";
        public string MaskedKey { get; init; } = "";
        public int UsageCount { get; init; }
        public string CreatedAtText { get; init; } = "";

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                OnPropertyChanged();
            }
        }

        public static ApiKeyRowViewModel From(LocalApiKey k) => new()
        {
            Id = k.Id,
            Name = k.Name,
            Key = k.Key,
            MaskedKey = LocalApiKeyService.MaskKey(k.Key),
            Enabled = k.Enabled,
            UsageCount = k.UsageCount,
            CreatedAtText = DateTimeOffset.FromUnixTimeMilliseconds(k.CreatedAt)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm")
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>新建 Key 名称输入。</summary>
public sealed class ApiKeyCreateDialog : Window
{
    public string KeyName { get; private set; } = "";

    public ApiKeyCreateDialog()
    {
        Title = "新建 API Key";
        Width = 400;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource("DsWindow");

        var root = new DockPanel { Margin = new Thickness(24) };
        var input = new TextBox { Style = (Style)Application.Current.FindResource("DsTextBox") };
        DockPanel.SetDock(input, Dock.Top);

        var label = new TextBlock
        {
            Text = "Key 名称",
            Style = (Style)Application.Current.FindResource("DsCaption"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        DockPanel.SetDock(label, Dock.Top);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button
        {
            Content = "取消",
            Style = (Style)Application.Current.FindResource("DsGhostButton"),
            MinWidth = 72,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var ok = new Button
        {
            Content = "创建",
            Style = (Style)Application.Current.FindResource("DsPrimaryButton"),
            MinWidth = 72,
            IsDefault = true
        };
        ok.Click += (_, _) =>
        {
            KeyName = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(KeyName))
            {
                DsMessageDialog.Warning(this, "请输入名称。", "新建 API Key");
                return;
            }

            DialogResult = true;
            Close();
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        root.Children.Add(input);
        root.Children.Add(label);
        Content = root;
        input.Focus();
    }
}
