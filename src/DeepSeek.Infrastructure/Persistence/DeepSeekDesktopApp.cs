using System.IO;

namespace DeepSeekBrowser.Services;

/// <summary>
/// 桌面端应用标识与路径（统一 deepseek_desktop 命名，替代旧 DeepSeekEdge / DeepSeek-Edge）。
/// </summary>
public static class DeepSeekDesktopApp
{
    public const string AppDataFolderName = "deepseek_desktop";
    public const string LegacyAppDataFolderName = "DeepSeekEdge";
    /// <summary>可选：复制 publish 到桌面时的文件夹名（需 build.ps1 -DeployToDesktop）。</summary>
    public const string DesktopInstallFolderName = "DeepSeek_desktop";

    /// <summary>仓库内唯一编译发布目录名（相对仓库根目录）。</summary>
    public const string PublishFolderName = "publish";
    public const string DisplayName = "DeepSeek Desktop";
    /// <summary>产品缩写（DeepSeek Desktop / DPDT）。</summary>
    public const string ProductAbbreviation = "DPDT";
    public const string JsLogPrefix = "DeepSeek Desktop";

    public const string ConfigDirEnvVar = "DEEPSEEK_DESKTOP_CONFIG_DIR";
    public const string LegacyConfigDirEnvVar = "DEEPSEEK_EDGE_CONFIG_DIR";

    public const string VerifyWorkModeEnvVar = "DEEPSEEK_DESKTOP_VERIFY_WORKMODE";
    public const string LegacyVerifyWorkModeEnvVar = "DEEPSEEK_EDGE_VERIFY_WORKMODE";

    public const string VerifyAgentEnvVar = "DEEPSEEK_DESKTOP_VERIFY_AGENT";
    public const string LegacyVerifyAgentEnvVar = "DEEPSEEK_EDGE_VERIFY_AGENT";

    public const string VerifyAgentTaskEnvVar = "DEEPSEEK_DESKTOP_VERIFY_AGENT_TASK";

    public const string VerifyShutdownEnvVar = "DEEPSEEK_DESKTOP_VERIFY_SHUTDOWN";

    /// <summary>与 VERIFY_WORKMODE 联用：自检完成后自动退出进程（供 verify-workmode-ui.ps1）。</summary>
    public const string VerifyWorkModeUiEnvVar = "DEEPSEEK_DESKTOP_VERIFY_WORKMODE_UI";

    /// <summary>桌面流畅度自检：校验注入 burst / Loading 次数（供 verify-desktop-smoothness.ps1）。</summary>
    public const string VerifySmoothnessEnvVar = "DEEPSEEK_DESKTOP_VERIFY_SMOOTHNESS";

    /// <summary>与 VERIFY_SMOOTHNESS 联用：自检完成后自动退出。</summary>
    public const string VerifySmoothnessUiEnvVar = "DEEPSEEK_DESKTOP_VERIFY_SMOOTHNESS_UI";

    /// <summary>设为 1 时启动器跳过运行库检测（开发/CI）。</summary>
    public const string SkipRuntimeCheckEnvVar = "DEEPSEEK_SKIP_RUNTIME_CHECK";

    public const string IntegrationMutexName = @"Global\deepseek_desktop.DsdTuiIntegration";
    public const string SingleInstanceMutexName = @"Global\DeepSeek.Desktop.SingleInstance";
    public const string SingleInstancePipeName = "DeepSeek.Desktop.SingleInstance.Activate";
    public const string LocalApiKeyFallback = "deepseek-local";

    private static bool _migrationChecked;

    public static string LocalAppDataRoot
    {
        get
        {
            EnsureLegacyAppDataMigrated();
            var overrideDir = ResolveEnv(ConfigDirEnvVar, LegacyConfigDirEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDataFolderName);
        }
    }

    public static string LogsDirectory => Path.Combine(LocalAppDataRoot, "logs");

    public static string WebViewUserDataDirectory => Path.Combine(LocalAppDataRoot, "User Data");

    public static string IntegrationFilePath =>
        Path.Combine(LocalAppDataRoot, "dsd-tui-integration.json");

    public static string ResolveEnv(string primary, string legacy)
    {
        var v = Environment.GetEnvironmentVariable(primary);
        if (!string.IsNullOrWhiteSpace(v))
            return v.Trim();

        v = Environment.GetEnvironmentVariable(legacy);
        return string.IsNullOrWhiteSpace(v) ? "" : v.Trim();
    }

    public static bool IsEnvEnabled(string primary, string legacy) =>
        string.Equals(ResolveEnv(primary, legacy), "1", StringComparison.Ordinal);

    public static void EnsureLegacyAppDataMigrated()
    {
        if (_migrationChecked)
            return;

        _migrationChecked = true;

        if (!string.IsNullOrWhiteSpace(ResolveEnv(ConfigDirEnvVar, LegacyConfigDirEnvVar)))
            return;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newRoot = Path.Combine(local, AppDataFolderName);
        var oldRoot = Path.Combine(local, LegacyAppDataFolderName);

        if (Directory.Exists(newRoot) || !Directory.Exists(oldRoot))
            return;

        try
        {
            Directory.Move(oldRoot, newRoot);
        }
        catch
        {
            try
            {
                Directory.CreateDirectory(newRoot);
                foreach (var entry in Directory.EnumerateFileSystemEntries(oldRoot))
                {
                    var name = Path.GetFileName(entry);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var dest = Path.Combine(newRoot, name);
                    if (Directory.Exists(entry))
                        CopyDirectory(entry, dest);
                    else
                        File.Copy(entry, dest, overwrite: true);
                }
            }
            catch
            {
                // 保留旧目录，新路径仍可用
            }
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
