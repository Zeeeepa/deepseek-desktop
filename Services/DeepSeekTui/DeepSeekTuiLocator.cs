namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>定位官方 <c>deepseek</c> dispatcher（见 deepseek-tui.com/zh/install）。</summary>
public static class DeepSeekTuiLocator
{
    public static string? Resolve(string? configuredPath) =>
        DeepSeekTuiBundle.ResolveDispatcher(configuredPath);
}
