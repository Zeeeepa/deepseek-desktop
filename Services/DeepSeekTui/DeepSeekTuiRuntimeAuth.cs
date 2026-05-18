using System.IO;
using System.Security.Cryptography;
using System.Text;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.DeepSeekTui;

/// <summary>
/// 本地 Runtime API 鉴权（<see href="https://github.com/Hmbown/DeepSeek-TUI/blob/main/docs/RUNTIME_API.md"/>）。
/// </summary>
public static class DeepSeekTuiRuntimeAuth
{
    private static string TokenFilePath =>
        Path.Combine(ConfigStore.ConfigDirectory, "tui-runtime.token");

    public static string Resolve(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DeepSeekTuiRuntimeToken))
            return config.DeepSeekTuiRuntimeToken.Trim();

        var env = Environment.GetEnvironmentVariable("DEEPSEEK_RUNTIME_TOKEN");
        if (!string.IsNullOrWhiteSpace(env))
        {
            var fromEnv = env.Trim();
            TryPersistToken(fromEnv);
            return fromEnv;
        }

        try
        {
            if (File.Exists(TokenFilePath))
            {
                var existing = File.ReadAllText(TokenFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                    return existing;
            }
        }
        catch
        {
            // ignore
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        TryPersistToken(token);
        return token;
    }

    private static void TryPersistToken(string token)
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            File.WriteAllText(TokenFilePath, token, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }
}
