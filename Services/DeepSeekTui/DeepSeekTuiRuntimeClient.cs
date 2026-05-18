using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DeepSeekBrowser.Services.DeepSeekTui;

public sealed class DeepSeekTuiRuntimeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string? _bearerToken;

    public DeepSeekTuiRuntimeClient(string baseUrl, string? runtimeBearerToken = null)
    {
        _bearerToken = string.IsNullOrWhiteSpace(runtimeBearerToken) ? null : runtimeBearerToken.Trim();
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.Timeout = TimeSpan.FromMinutes(30);
        if (_bearerToken is not null)
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _bearerToken);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-DeepSeek-Runtime-Token", _bearerToken);
        }
    }

    public async Task<string> CreateThreadAsync(
        string? workspace,
        string mode,
        string model,
        bool autoApprove,
        bool allowShell,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["mode"] = mode,
            ["auto_approve"] = autoApprove,
            ["allow_shell"] = allowShell,
        };
        if (!string.IsNullOrWhiteSpace(workspace))
            body["workspace"] = workspace;

        using var resp = await PostJsonAsync("v1/threads", body, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct)
            .ConfigureAwait(false);
        return doc.RootElement.GetProperty("id").GetString()
               ?? throw new InvalidOperationException("创建线程失败：缺少 id");
    }

    public async Task<string> StartTurnAsync(string threadId, string prompt, CancellationToken ct)
    {
        var body = new { prompt };
        using var resp = await PostJsonAsync($"v1/threads/{Uri.EscapeDataString(threadId)}/turns", body, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct)
            .ConfigureAwait(false);
        if (doc.RootElement.TryGetProperty("turn", out var turn) &&
            turn.TryGetProperty("id", out var turnId))
            return turnId.GetString() ?? "";
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    public async Task DecideApprovalAsync(string approvalId, bool allow, CancellationToken ct)
    {
        var body = new { decision = allow ? "allow" : "deny", remember = false };
        using var resp = await PostJsonAsync($"v1/approvals/{Uri.EscapeDataString(approvalId)}", body, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
    }

    public async Task InterruptTurnAsync(string threadId, string turnId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            return;

        using var resp = await PostJsonAsync(
                $"v1/threads/{Uri.EscapeDataString(threadId)}/turns/{Uri.EscapeDataString(turnId)}/interrupt",
                new { },
                ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<RuntimeSseEvent> StreamEventsAsync(
        string threadId,
        ulong sinceSeq,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var url = WithRuntimeTokenQuery(
            $"v1/threads/{Uri.EscapeDataString(threadId)}/events?since_seq={sinceSeq}");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(resp, ct).ConfigureAwait(false);

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        string? eventName = null;
        var dataBuf = new StringBuilder();

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
                break;

            if (line.Length == 0)
            {
                if (dataBuf.Length > 0)
                {
                    var json = dataBuf.ToString();
                    dataBuf.Clear();
                    yield return ParseSseEvent(eventName, json);
                }

                eventName = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
                eventName = line["event:".Length..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal))
                dataBuf.AppendLine(line["data:".Length..].Trim());
        }

        if (dataBuf.Length > 0)
            yield return ParseSseEvent(eventName, dataBuf.ToString());
    }

    private static RuntimeSseEvent ParseSseEvent(string? eventName, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ev = root.TryGetProperty("event", out var evEl)
                ? evEl.GetString()
                : eventName;
            JsonElement payload = root;
            if (root.TryGetProperty("payload", out var pl))
                payload = pl;
            return new RuntimeSseEvent(ev ?? "", payload.Clone());
        }
        catch
        {
            return new RuntimeSseEvent(eventName ?? "", default);
        }
    }

    private Task<HttpResponseMessage> PostJsonAsync(string path, object body, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return _http.PostAsync(WithRuntimeTokenQuery(path), content, ct);
    }

    private string WithRuntimeTokenQuery(string path)
    {
        if (_bearerToken is null)
            return path;
        return path.Contains('?', StringComparison.Ordinal)
            ? $"{path}&token={Uri.EscapeDataString(_bearerToken)}"
            : $"{path}?token={Uri.EscapeDataString(_bearerToken)}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"DeepSeek-TUI API {(int)resp.StatusCode}: {Truncate(body, 500)}");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

public readonly record struct RuntimeSseEvent(string Name, JsonElement Payload);
