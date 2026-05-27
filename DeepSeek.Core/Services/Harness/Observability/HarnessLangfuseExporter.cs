using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Observability;

/// <summary>将本地 trace.jsonl 导出到 Langfuse Cloud Public Ingestion API。</summary>
public static class HarnessLangfuseExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static bool IsConfigured(AppConfig config) =>
        config.AgentLangfuseEnabled
        && !string.IsNullOrWhiteSpace(config.AgentLangfusePublicKey)
        && !string.IsNullOrWhiteSpace(config.AgentLangfuseSecretKey);

    public static async Task<bool> PingAsync(AppConfig config, CancellationToken ct = default)
    {
        if (!IsConfigured(config))
            return false;

        var host = NormalizeHost(config.AgentLangfuseHost);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{host}/api/public/health");
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    config.AgentLangfusePublicKey + ":" + config.AgentLangfuseSecretKey)));
            using var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static string BuildTraceUrl(AppConfig config, string traceId)
    {
        var host = NormalizeHost(config.AgentLangfuseHost);
        var project = config.AgentLangfuseProject?.Trim();
        if (string.IsNullOrWhiteSpace(project))
            return $"{host}/trace/{traceId}";
        return $"{host}/project/{Uri.EscapeDataString(project)}/traces/{Uri.EscapeDataString(traceId)}";
    }

    public static async Task<bool> TryExportAsync(string runDir, AppConfig config, CancellationToken ct = default)
    {
        if (!IsConfigured(config) || !Directory.Exists(runDir))
            return false;

        var metaPath = Path.Combine(runDir, "meta.json");
        var tracePath = Path.Combine(runDir, "trace.jsonl");
        if (!File.Exists(metaPath))
            return false;

        HarnessRunMeta? meta;
        try
        {
            meta = JsonSerializer.Deserialize<HarnessRunMeta>(await File.ReadAllTextAsync(metaPath, ct), JsonOptions);
        }
        catch
        {
            return false;
        }

        if (meta is null || string.IsNullOrWhiteSpace(meta.TraceId))
            return false;

        var spans = new List<HarnessTraceSpan>();
        if (File.Exists(tracePath))
        {
            foreach (var line in await File.ReadAllLinesAsync(tracePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var span = JsonSerializer.Deserialize<HarnessTraceSpan>(line, JsonOptions);
                    if (span is not null) spans.Add(span);
                }
                catch
                {
                    // skip bad line
                }
            }
        }

        var batch = BuildBatch(meta, spans);
        if (batch.Count == 0) return false;

        var host = NormalizeHost(config.AgentLangfuseHost);
        var url = host.TrimEnd('/') + "/api/public/ingestion";
        var payload = JsonSerializer.Serialize(new { batch }, JsonOptions);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            config.AgentLangfusePublicKey + ":" + config.AgentLangfuseSecretKey));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

        try
        {
            using var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                AgentDebugLogger.Current?.Write("LANGFUSE",
                    $"export failed {(int)res.StatusCode} {await res.Content.ReadAsStringAsync(ct)}");
                return false;
            }
            AgentDebugLogger.Current?.Write("LANGFUSE", $"exported trace {meta.TraceId} events={batch.Count}");
            return true;
        }
        catch (Exception ex)
        {
            AgentDebugLogger.Current?.Write("LANGFUSE", "export error " + ex.Message);
            return false;
        }
    }

    public static List<IngestionEvent> BuildBatch(HarnessRunMeta meta, IReadOnlyList<HarnessTraceSpan> spans)
    {
        var batch = new List<IngestionEvent>();
        var traceId = meta.TraceId;
        var ts = meta.StartedUtc;
        if (string.IsNullOrWhiteSpace(ts))
            ts = DateTime.UtcNow.ToString("O");

        batch.Add(new IngestionEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = "trace-create",
            Timestamp = ts,
            Body = new Dictionary<string, object?>
            {
                ["id"] = traceId,
                ["name"] = meta.Strategy ?? "harness-run",
                ["timestamp"] = ts,
                ["metadata"] = new Dictionary<string, object?>
                {
                    ["runId"] = meta.RunId,
                    ["sessionId"] = meta.SessionId,
                    ["model"] = meta.Model
                }
            }
        });

        string? rootSpanId = null;
        foreach (var span in spans)
        {
            if (span.Name == "run.root")
            {
                rootSpanId = span.SpanId;
                continue;
            }

            batch.Add(new IngestionEvent
            {
                Id = Guid.NewGuid().ToString(),
                Type = "span-create",
                Timestamp = span.StartUtc,
                Body = new Dictionary<string, object?>
                {
                    ["id"] = span.SpanId,
                    ["traceId"] = traceId,
                    ["parentObservationId"] = span.ParentSpanId ?? rootSpanId,
                    ["name"] = span.Name,
                    ["startTime"] = span.StartUtc,
                    ["endTime"] = span.EndUtc ?? span.StartUtc,
                    ["metadata"] = span.Attributes
                }
            });
        }

        if (meta.TotalTokens > 0 && rootSpanId is not null)
        {
            batch.Add(new IngestionEvent
            {
                Id = Guid.NewGuid().ToString(),
                Type = "generation-create",
                Timestamp = meta.EndedUtc ?? ts,
                Body = new Dictionary<string, object?>
                {
                    ["id"] = "gen-" + meta.RunId,
                    ["traceId"] = traceId,
                    ["parentObservationId"] = rootSpanId,
                    ["name"] = "llm-aggregate",
                    ["startTime"] = ts,
                    ["endTime"] = meta.EndedUtc ?? ts,
                    ["model"] = meta.Model,
                    ["usage"] = new Dictionary<string, object?>
                    {
                        ["input"] = meta.PromptTokens,
                        ["output"] = meta.CompletionTokens,
                        ["total"] = meta.TotalTokens
                    }
                }
            });
        }

        return batch;
    }

    /// <summary>Live export: push a single span as it completes (requires AgentLangfuseLiveExport).</summary>
    public static async Task<bool> TryExportSpanLiveAsync(
        HarnessTraceSpan span,
        string runId,
        string traceId,
        AppConfig config,
        CancellationToken ct = default)
    {
        if (!IsConfigured(config))
            return false;

        try
        {
            var host = NormalizeHost(config.AgentLangfuseHost);
            var batch = new List<IngestionEvent>
            {
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = "span-create",
                    Timestamp = span.StartUtc,
                    Body = new Dictionary<string, object?>
                    {
                        ["id"] = span.SpanId,
                        ["traceId"] = traceId,
                        ["parentObservationId"] = span.ParentSpanId,
                        ["name"] = span.Name,
                        ["startTime"] = span.StartUtc,
                        ["endTime"] = span.EndUtc ?? span.StartUtc,
                        ["metadata"] = new Dictionary<string, object?>
                        {
                            ["runId"] = runId,
                            ["status"] = span.Status
                        }
                    }
                }
            };

            var url = host + "/api/public/ingestion";
            var payload = JsonSerializer.Serialize(new { batch }, JsonOptions);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                config.AgentLangfusePublicKey + ":" + config.AgentLangfuseSecretKey));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            using var res = await Http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeHost(string? host)
    {
        var h = string.IsNullOrWhiteSpace(host) ? "https://cloud.langfuse.com" : host.Trim();
        if (!h.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            h = "https://" + h;
        return h.TrimEnd('/');
    }

    public sealed class IngestionEvent
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public Dictionary<string, object?> Body { get; set; } = new();
    }
}
