using System.Diagnostics;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>对齐 Chat2API oauthManager.startLogin（默认浏览器打开登录页）。</summary>
public static class DsdOAuthBrowserLogin
{
    public static Task<object> StartLoginAsync(string providerId, string providerType)
    {
        var typeKey = string.IsNullOrWhiteSpace(providerType) ? providerId : providerType;
        var loginUrl = BuiltinProviderCatalog.Find(providerId)?.LoginUrl
                       ?? BuiltinProviderCatalog.Find(typeKey)?.LoginUrl
                       ?? ResolveLoginUrl(typeKey);

        try
        {
            Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return Task.FromResult<object>(new
            {
                success = false,
                providerId,
                providerType = typeKey,
                error = ex.Message
            });
        }

        var manualHint = typeKey.ToLowerInvariant() switch
        {
            "glm" => "请在浏览器登录后，从开发者工具提取 chatglm_refresh_token 并手动填写",
            "deepseek" => "请在浏览器登录后，从开发者工具 Application → Local Storage 提取 userToken 并手动填写",
            _ => "请在浏览器完成登录后，从开发者工具提取凭证并手动填写"
        };

        return Task.FromResult<object>(new
        {
            success = false,
            providerId,
            providerType = typeKey,
            error = manualHint
        });
    }

    private static string ResolveLoginUrl(string typeKey) => typeKey.ToLowerInvariant() switch
    {
        "glm" => "https://chatglm.cn",
        "kimi" => "https://www.kimi.com",
        "minimax" => "https://agent.minimaxi.com",
        "qwen" => "https://www.qianwen.com",
        "qwen-ai" => "https://chat.qwen.ai",
        "perplexity" => "https://www.perplexity.ai",
        "mimo" => "https://aistudio.xiaomimimo.com",
        "zai" => "https://chat.z.ai",
        _ => "https://chat.deepseek.com"
    };
}
