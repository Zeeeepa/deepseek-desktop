using System.Windows;
using DeepSeekBrowser.Services.ApiManagement;
using DeepSeekBrowser.Services.OAuth;
using DeepSeekBrowser.Views;

namespace DeepSeekBrowser.Services;

/// <summary>
/// DeepSeek Desktop 内嵌 OAuth 弹窗（WebView2 对齐 Chat2API <c>inAppLogin.ts</c>）。
/// </summary>
public sealed class DsdOAuthInAppLoginService
{
    private readonly Func<Window?> _owner;
    private readonly WebInjectService _web;
    private readonly LocalOpenAiServer _localApi;
    private OAuthLoginWindow? _activeWindow;

    public DsdOAuthInAppLoginService(
        Func<Window?> owner,
        WebInjectService web,
        LocalOpenAiServer localApi)
    {
        _owner = owner;
        _web = web;
        _localApi = localApi;
    }

    public bool IsOpen => _activeWindow is not null;

    public async Task<object> StartAsync(
        string providerId,
        string providerType,
        CancellationToken ct = default,
        Action<string, string>? onProgress = null)
    {
        if (_activeWindow is not null)
        {
            return new
            {
                success = false,
                providerId,
                providerType,
                error = "A login window is already open"
            };
        }

        onProgress?.Invoke("pending", "Opening login window...");

        var typeKey = string.IsNullOrWhiteSpace(providerType) ? providerId : providerType;
        var config = DsdOAuthTokenExtraction.GetConfig(typeKey);
        if (config is null)
        {
            return new
            {
                success = false,
                providerId,
                providerType = typeKey,
                error = $"不支持的 OAuth 供应商：{typeKey}"
            };
        }

        var owner = _owner();
        var loginResult = await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var window = new OAuthLoginWindow(config, providerId, owner);
            _activeWindow = window;
            window.Closed += (_, _) => _activeWindow = null;
            window.Show();
            return await window.WaitForResultAsync(ct);
        }).Task.Unwrap();

        if (!loginResult.Success)
        {
            onProgress?.Invoke("error", MapErrorMessage(loginResult.Error));
            return new
            {
                success = false,
                providerId,
                providerType = typeKey,
                error = MapErrorMessage(loginResult.Error)
            };
        }

        onProgress?.Invoke("pending", "Validating credentials...");
        var raw = loginResult.Credentials ?? new Dictionary<string, string>();
        var credentials = DsdOAuthCredentialMapper.Map(providerId, raw);

        if (string.Equals(providerId, "deepseek", StringComparison.OrdinalIgnoreCase)
            && credentials.TryGetValue("token", out var token)
            && !string.IsNullOrWhiteSpace(token))
        {
            var check = await DeepSeekWebTokenValidator.ValidateAsync(token.Trim(), ct);
            if (!check.Valid)
            {
                onProgress?.Invoke("error", check.Error ?? "Token 无效");
                return new
                {
                    success = false,
                    providerId,
                    providerType = typeKey,
                    error = check.Error ?? "Token 无效"
                };
            }

            onProgress?.Invoke("success", "Login successful");
            var displayName = !string.IsNullOrWhiteSpace(check.Name)
                ? check.Name!
                : BuiltinProviderCatalog.Find(providerId)?.Name ?? providerId;
            return new
            {
                success = true,
                providerId,
                providerType = typeKey,
                credentials,
                accountInfo = new { name = displayName, email = check.Email ?? "" }
            };
        }

        onProgress?.Invoke("success", "Login successful");
        return new
        {
            success = true,
            providerId,
            providerType = typeKey,
            credentials,
            accountInfo = new { name = BuiltinProviderCatalog.Find(providerId)?.Name ?? providerId, email = "" }
        };
    }

    public void Cancel()
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _activeWindow?.Close();
            }
            catch
            {
                // ignore
            }
        });
    }

    private static string MapErrorMessage(string? error) =>
        error switch
        {
            "Login window was closed" => "登录窗口已关闭",
            "A login window is already open" => "已有登录窗口打开",
            "Login cancelled by user" or "登录已取消" => "登录已取消",
            null or "" => "登录失败",
            _ => error
        };
}
