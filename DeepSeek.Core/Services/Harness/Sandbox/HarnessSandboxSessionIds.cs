using System.Security.Cryptography;
using System.Text;
using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Services.Harness.Sandbox;

public static class HarnessSandboxSessionIds
{
    /// <summary>从稳定键生成确定性 sandbox session id（跨 Run 复用同一 Provider 槽位）。</summary>
    public static string Deterministic(string stableKey)
    {
        if (string.IsNullOrWhiteSpace(stableKey))
            return "sbx-" + Guid.NewGuid().ToString("N")[..12];

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stableKey.Trim()));
        return "sbx-" + Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public static string ResolveStableKey(HarnessRunState state)
    {
        if (!string.IsNullOrWhiteSpace(state.RunId))
            return "run:" + state.RunId.Trim();
        if (!string.IsNullOrWhiteSpace(state.WebChatSessionId))
            return "web:" + state.WebChatSessionId.Trim();
        if (!string.IsNullOrWhiteSpace(state.SandboxSessionId))
            return "sbx:" + state.SandboxSessionId.Trim();
        return "";
    }
}
