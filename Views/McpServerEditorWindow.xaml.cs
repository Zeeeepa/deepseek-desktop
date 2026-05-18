using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Views;

public partial class McpServerEditorWindow : System.Windows.Window
{
    private readonly string? _existingId;
    private bool _suppressTransportEvent;

    public McpServerConfig? Result { get; private set; }

    public McpServerEditorWindow(McpServerConfig? existing = null)
    {
        InitializeComponent();

        if (existing is not null)
        {
            _existingId = existing.Id;
            ApplyConfig(existing);
        }
        else
        {
            NameBox.Text = "我的 MCP 服务";
            CommandBox.Text = "npx";
            ArgsBox.Text = "-y" + Environment.NewLine + "@modelcontextprotocol/server-filesystem";
            SelectTransport("stdio");
        }
    }

    private void ApplyConfig(McpServerConfig cfg)
    {
        NameBox.Text = cfg.Name;
        SelectTransport(cfg.IsRemote ? "remote" : "stdio");
        UrlBox.Text = cfg.Url ?? "";
        CommandBox.Text = cfg.Command;
        ArgsBox.Text = string.Join(Environment.NewLine, cfg.Arguments);
        WorkDirBox.Text = cfg.WorkingDirectory ?? "";
        EnvBox.Text = string.Join(Environment.NewLine,
            cfg.Environment.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private void SelectTransport(string type)
    {
        _suppressTransportEvent = true;
        var remote = type.Equals("remote", StringComparison.OrdinalIgnoreCase)
            || type.Equals("http", StringComparison.OrdinalIgnoreCase);
        StdioModeBtn.IsChecked = !remote;
        RemoteModeBtn.IsChecked = remote;
        _suppressTransportEvent = false;
        UpdateTransportPanels();
    }

    private void TransportMode_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressTransportEvent) return;

        _suppressTransportEvent = true;
        if (sender == RemoteModeBtn)
        {
            RemoteModeBtn.IsChecked = true;
            StdioModeBtn.IsChecked = false;
        }
        else
        {
            StdioModeBtn.IsChecked = true;
            RemoteModeBtn.IsChecked = false;
        }

        _suppressTransportEvent = false;
        UpdateTransportPanels();
    }

    private void UpdateTransportPanels()
    {
        var remote = RemoteModeBtn.IsChecked == true;
        RemotePanel.Visibility = remote ? Visibility.Visible : Visibility.Collapsed;
        StdioPanel.Visibility = remote ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            DsMessageDialog.Warning(this, "请填写名称。", "提示");
            return;
        }

        var isRemote = RemoteModeBtn.IsChecked == true;
        if (isRemote)
        {
            if (string.IsNullOrWhiteSpace(UrlBox.Text))
            {
                DsMessageDialog.Warning(this, "请填写远程 MCP 的 URL。", "提示");
                return;
            }

            if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not "http" and not "https")
            {
                DsMessageDialog.Warning(this, "URL 须为 http:// 或 https:// 开头的完整地址。", "提示");
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(CommandBox.Text))
        {
            DsMessageDialog.Warning(this, "请填写启动命令。", "提示");
            return;
        }

        Result = new McpServerConfig
        {
            Id = _existingId ?? Guid.NewGuid().ToString("N")[..8],
            Name = NameBox.Text.Trim(),
            TransportType = isRemote ? "http" : "stdio",
            Url = isRemote ? McpRemoteEndpoint.Resolve(UrlBox.Text).ToString() : null,
            Command = CommandBox.Text.Trim(),
            Arguments = ArgsBox.Text
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            WorkingDirectory = string.IsNullOrWhiteSpace(WorkDirBox.Text) ? null : WorkDirBox.Text.Trim(),
            Environment = ParseEnvironment(EnvBox.Text),
            Enabled = true
        };
        DialogResult = true;
    }

    private static Dictionary<string, string> ParseEnvironment(string text)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
                env[key] = value;
        }

        return env;
    }
}
