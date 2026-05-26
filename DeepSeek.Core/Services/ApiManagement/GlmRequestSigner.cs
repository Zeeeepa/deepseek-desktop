using System.Security.Cryptography;
using System.Text;

namespace DeepSeekBrowser.Services.ApiManagement;

/// <summary>智谱 GLM 网页 API 签名（对齐 Chat2API ProviderChecker.generateGLMSignV2）。</summary>
public static class GlmRequestSigner
{
    private const string Secret = "8a1317a7468aa3ad86e997d08f3f31cb";

    public sealed record SignResult(string Timestamp, string Nonce, string Sign);

    public static SignResult Create()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var len = now.Length;
        var digits = now.Select(c => c - '0').ToArray();
        var sum = digits.Sum() - digits[len - 2];
        var checkDigit = sum % 10;
        var timestamp = now[..(len - 2)] + checkDigit + now[(len - 1)..];
        var nonce = Guid.NewGuid().ToString("N");
        var sign = Md5Hex($"{timestamp}-{nonce}-{Secret}");
        return new SignResult(timestamp, nonce, sign);
    }

    public static string Md5Hex(string input)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
