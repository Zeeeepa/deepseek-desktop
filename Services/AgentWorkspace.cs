using System.IO;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>Agent / DeepSeek-TUI 工作区根目录。</summary>
public static class AgentWorkspace
{
    public static string ResolveRoot(AppConfig config)
    {
        var root = config.AgentWorkspaceRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.GetFullPath(root);
    }
}
