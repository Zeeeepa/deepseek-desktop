using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness.Interop;

/// <summary>
/// 市场主流工具/Skill 路径约定（DSD 自研 Harness 的互操作扫描根目录）。
/// </summary>
public static class HarnessInteropPaths
{
    public static IEnumerable<string> SkillScanRoots(string? workspaceRoot)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        yield return Path.Combine(AgentDesktopConfigSync.HomeDirectory, "skills");
        yield return Path.Combine(home, ".cursor", "skills");
        yield return Path.Combine(home, ".claude", "skills");
        yield return Path.Combine(home, ".codex", "skills");

        if (string.IsNullOrWhiteSpace(workspaceRoot)) yield break;

        yield return Path.Combine(workspaceRoot, ".deepseek", "skills");
        yield return Path.Combine(workspaceRoot, ".cursor", "skills");
        yield return Path.Combine(workspaceRoot, ".agents", "skills");
    }

    public static IEnumerable<string> McpConfigPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return AgentDesktopConfigSync.McpPath;
        yield return Path.Combine(home, ".cursor", "mcp.json");
        yield return Path.Combine(home, ".claude", "claude_desktop_config.json");
        yield return Path.Combine(home, ".config", "claude", "claude_desktop_config.json");
    }
}
