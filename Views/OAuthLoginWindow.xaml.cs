using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DeepSeekBrowser.Services.OAuth;
using Microsoft.Web.WebView2.Core;

namespace DeepSeekBrowser.Views;

public partial class OAuthLoginWindow : Window
{
    private const int MinLoginMs = 5000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DsdOAuthTokenExtraction.Config _config;
    private readonly string _providerId;
    private readonly Dictionary<string, string> _collected = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource<DsdOAuthInAppLoginResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private DispatcherTimer? _pollTimer;
    private DateTime _loginStartUtc;
    private bool _completed;
    private bool _validating;
    private CoreWebView2Environment? _environment;

    public OAuthLoginWindow(DsdOAuthTokenExtraction.Config config, string providerId, Window? owner)
    {
        _config = config;
        _providerId = providerId;
        InitializeComponent();
        Title = config.WindowTitle;
        if (owner is not null)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    public Task<DsdOAuthInAppLoginResult> WaitForResultAsync(CancellationToken ct)
    {
        ct.Register(() =>
        {
            Dispatcher.BeginInvoke(() => Complete(new DsdOAuthInAppLoginResult(false, Error: "登录已取消")));
        });
        return _tcs.Task;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        try
        {
            var sessionDir = Path.Combine(
                Services.DeepSeekDesktopApp.LocalAppDataRoot,
                "oauth-sessions",
                _providerId,
                DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(sessionDir);

            _environment = await CoreWebView2Environment.CreateAsync(null, sessionDir);
            await OAuthWebView.EnsureCoreWebView2Async(_environment);
            var core = OAuthWebView.CoreWebView2!;

            core.Settings.IsScriptEnabled = true;
            core.Settings.AreDefaultScriptDialogsEnabled = true;
            core.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrWhiteSpace(args.Uri))
                    core.Navigate(args.Uri);
            };

            if (_config.TokenSources.Any(s => s.Kind == DsdOAuthTokenExtraction.TokenSourceKind.NetworkHeader))
            {
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
            }

            _loginStartUtc = DateTime.UtcNow;
            core.NavigationCompleted += (_, _) => SchedulePoll(immediate: true);

            _pollTimer = new DispatcherTimer { Interval = PollInterval };
            _pollTimer.Tick += (_, _) => _ = PollTokensAsync();
            _pollTimer.Start();

            core.Navigate(_config.LoginUrl);
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Complete(new DsdOAuthInAppLoginResult(false, Error: $"无法打开登录窗口：{ex.Message}"));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_completed)
            Complete(new DsdOAuthInAppLoginResult(false, Error: "Login window was closed"));
        _pollTimer?.Stop();
        base.OnClosed(e);
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_completed || !MinTimePassed())
            return;

        foreach (var source in _config.TokenSources)
        {
            if (source.Kind != DsdOAuthTokenExtraction.TokenSourceKind.NetworkHeader)
                continue;
            if (!e.Request.Headers.Contains("Authorization"))
                continue;

            var auth = e.Request.Headers.GetHeader("Authorization");
            var token = auth;
            if (!string.IsNullOrWhiteSpace(source.HeaderPattern))
            {
                var m = System.Text.RegularExpressions.Regex.Match(
                    auth,
                    source.HeaderPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && m.Groups.Count > 1)
                    token = m.Groups[1].Value;
                else if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = auth[7..];
            }
            else if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = auth[7..];
            }

            if (DsdOAuthTokenValidator.IsValidToken(token))
                OnTokenFound(source.Key, token.Trim());
        }
    }

    private void SchedulePoll(bool immediate)
    {
        var delay = immediate ? TimeSpan.FromSeconds(1) : PollInterval;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(delay);
            await PollTokensAsync();
        }, DispatcherPriority.Background);
    }

    private bool MinTimePassed() =>
        (DateTime.UtcNow - _loginStartUtc).TotalMilliseconds >= MinLoginMs;

    private async Task PollTokensAsync()
    {
        if (_completed || OAuthWebView.CoreWebView2 is null || !MinTimePassed())
            return;

        var core = OAuthWebView.CoreWebView2;

        foreach (var source in _config.TokenSources.Where(s => s.Kind == DsdOAuthTokenExtraction.TokenSourceKind.LocalStorage))
        {
            var key = source.Key.Replace("'", "\\'");
            var raw = await core.ExecuteScriptAsync($"localStorage.getItem('{key}')");
            var value = UnquoteJsonString(raw);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (string.Equals(source.Key, "user_detail_agent", StringComparison.OrdinalIgnoreCase))
            {
                TryParseMiniMaxUserDetail(value);
                continue;
            }

            var tokenValue = UnwrapJsonTokenValue(value);
            if (DsdOAuthTokenValidator.IsValidToken(tokenValue))
            {
                var emitKey = source.Key is "_token" or "userToken" ? "token" : source.Key;
                OnTokenFound(emitKey, tokenValue);
            }
        }

        foreach (var source in _config.TokenSources.Where(s => s.Kind == DsdOAuthTokenExtraction.TokenSourceKind.Cookie))
        {
            var cookies = await GetAllCookiesAsync(core);
            var hit = cookies.FirstOrDefault(c =>
                string.Equals(c.Name, source.Key, StringComparison.OrdinalIgnoreCase));
            if (hit is null || string.IsNullOrWhiteSpace(hit.Value))
                continue;
            if (!DsdOAuthTokenValidator.IsValidToken(hit.Value))
                continue;

            foreach (var c in cookies)
            {
                if (!string.IsNullOrWhiteSpace(c.Value))
                    _collected[c.Name] = c.Value;
            }

            OnTokenFound(source.Key, hit.Value);
        }
    }

    private async Task<List<CoreWebView2Cookie>> GetAllCookiesAsync(CoreWebView2 core)
    {
        var list = new List<CoreWebView2Cookie>();
        var manager = core.CookieManager;
        var uris = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _config.LoginUrl };
        foreach (var domain in _config.TargetDomains)
        {
            var host = domain.TrimStart('.');
            uris.Add($"https://{host}/");
        }

        foreach (var uri in uris)
        {
            try
            {
                var batch = await manager.GetCookiesAsync(uri);
                foreach (var c in batch)
                {
                    if (!list.Any(x => x.Name == c.Name && x.Domain == c.Domain))
                        list.Add(c);
                }
            }
            catch
            {
                // ignore per-domain failures
            }
        }

        return list;
    }

    private void TryParseMiniMaxUserDetail(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("realUserID", out var rid))
                OnTokenFound("realUserID", rid.ToString());
            else if (root.TryGetProperty("id", out var id))
                OnTokenFound("realUserID", id.ToString());
        }
        catch
        {
            // ignore
        }
    }

    private void OnTokenFound(string key, string value)
    {
        if (_completed || string.IsNullOrWhiteSpace(value) || !MinTimePassed())
            return;

        if (string.Equals(_providerId, "deepseek", StringComparison.OrdinalIgnoreCase)
            && !IsDeepSeekLoggedInPage())
            return;

        _collected[key] = value.Trim();
        _ = TryValidateAndCompleteAsync();
    }

    private bool IsDeepSeekLoggedInPage()
    {
        var src = OAuthWebView.CoreWebView2?.Source ?? "";
        if (string.IsNullOrWhiteSpace(src))
            return false;
        var lower = src.ToLowerInvariant();
        if (lower.Contains("/sign_in") || lower.Contains("/sign-in") || lower.Contains("/login"))
            return false;
        return lower.Contains("chat.deepseek.com", StringComparison.OrdinalIgnoreCase);
    }

    private async Task TryValidateAndCompleteAsync()
    {
        if (_completed || _validating)
            return;

        var provider = _providerId.ToLowerInvariant();
        if (provider == "minimax")
        {
            if (!_collected.ContainsKey("token"))
                return;
            if (!_collected.ContainsKey("realUserID"))
            {
                await Task.Delay(500);
                if (!_collected.ContainsKey("realUserID"))
                    return;
            }
        }
        else if (provider == "mimo")
        {
            var hasService = _collected.ContainsKey("serviceToken") || _collected.ContainsKey("service_token");
            var hasUser = _collected.ContainsKey("userId") || _collected.ContainsKey("user_id");
            var hasPh = _collected.ContainsKey("xiaomichatbot_ph") || _collected.ContainsKey("ph_token");
            if (!hasService || !hasUser || !hasPh)
            {
                await Task.Delay(500);
                return;
            }
        }

        _validating = true;
        try
        {
            if (string.Equals(_providerId, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                if (!_collected.TryGetValue("token", out var userToken)
                    && !_collected.TryGetValue("userToken", out userToken))
                {
                    return;
                }

                var check = await DeepSeekWebTokenValidator.ValidateAsync(userToken);
                if (!check.Valid)
                {
                    _collected.Remove("token");
                    _collected.Remove("userToken");
                    return;
                }

                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["token"] = userToken.Trim()
                };
                Complete(new DsdOAuthInAppLoginResult(true, Credentials: snapshot));
                return;
            }

            var all = new Dictionary<string, string>(_collected, StringComparer.OrdinalIgnoreCase);
            Complete(new DsdOAuthInAppLoginResult(true, Credentials: all));
        }
        finally
        {
            _validating = false;
        }
    }

    private void Complete(DsdOAuthInAppLoginResult result)
    {
        if (_completed)
            return;
        _completed = true;
        _pollTimer?.Stop();
        _tcs.TrySetResult(result);
        try
        {
            Close();
        }
        catch
        {
            // ignore
        }
    }

    private static string? UnquoteJsonString(string? jsonQuoted)
    {
        if (string.IsNullOrWhiteSpace(jsonQuoted) || jsonQuoted == "null")
            return null;
        try
        {
            return JsonSerializer.Deserialize<string>(jsonQuoted);
        }
        catch
        {
            var t = jsonQuoted.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                return t[1..^1];
            return t;
        }
    }

    private static string UnwrapJsonTokenValue(string value)
    {
        value = value.Trim();
        if (!value.StartsWith('{') || !value.EndsWith('}'))
            return value;
        try
        {
            using var doc = JsonDocument.Parse(value);
            if (doc.RootElement.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? value;
        }
        catch
        {
            // ignore
        }

        return value;
    }
}

public sealed record DsdOAuthInAppLoginResult(
    bool Success,
    IReadOnlyDictionary<string, string>? Credentials = null,
    string? Error = null);
