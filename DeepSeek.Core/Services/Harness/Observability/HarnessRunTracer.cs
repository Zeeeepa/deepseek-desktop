using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services.Harness.Observability;

public sealed class HarnessRunTracer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _runDir;
    private readonly string _tracePath;
    private readonly AppConfig? _config;
    private readonly object _lock = new();
    private readonly Stopwatch _runSw = Stopwatch.StartNew();
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    private int _promptTokens;
    private int _completionTokens;
    private int _toolCallCount;
    private int _toolErrorCount;
    private int _llmCallCount;
    private string _tokenSource = "unknown";
    private string? _model;
    private bool _finalized;

    public string RunId { get; }
    public string TraceId { get; }
    public string? RootSpanId { get; private set; }
    public string? CurrentSpanId { get; private set; }

    private HarnessRunTracer(string workspaceRoot, string runId, HarnessRunTracerContext ctx, AppConfig? config)
    {
        RunId = runId;
        TraceId = "trace-" + Guid.NewGuid().ToString("N")[..12];
        _config = config;
        _runDir = Path.Combine(workspaceRoot, ".deepseek", "runs", runId);
        Directory.CreateDirectory(_runDir);
        _tracePath = Path.Combine(_runDir, "trace.jsonl");

        _model = ctx.Model;
        RootSpanId = StartSpanInternal("run.root", null, new Dictionary<string, object?>
        {
            ["strategy"] = ctx.Strategy,
            ["sessionId"] = ctx.SessionId,
            ["promptPreview"] = Truncate(ctx.PromptPreview, 200)
        });
        CurrentSpanId = RootSpanId;
    }

    public static HarnessRunTracer? TryBegin(string workspaceRoot, string runId, AppConfig config, HarnessRunTracerContext ctx)
    {
        if (!config.AgentStructuredTraceEnabled)
            return null;
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(runId))
            return null;
        return new HarnessRunTracer(workspaceRoot, runId, ctx, config);
    }

    public string RunDirectory => _runDir;

    public long ElapsedMilliseconds => _runSw.ElapsedMilliseconds;
    public int PromptTokens { get { lock (_lock) return _promptTokens; } }
    public int CompletionTokens { get { lock (_lock) return _completionTokens; } }
    public int LlmCallCount { get { lock (_lock) return _llmCallCount; } }
    public int ToolCallCount { get { lock (_lock) return _toolCallCount; } }

    public HarnessActiveSpan StartSpan(string name, string? parentSpanId = null, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        var spanId = StartSpanInternal(name, parentSpanId ?? CurrentSpanId, attributes);
        return new HarnessActiveSpan(this, spanId);
    }

    public void RecordLlmUsage(WebChatResult result, string inferenceSource)
    {
        lock (_lock)
        {
            _llmCallCount++;
            if (!string.IsNullOrWhiteSpace(result.Model))
                _model = result.Model;
            if (result.PromptTokens > 0 || result.CompletionTokens > 0)
            {
                _promptTokens += result.PromptTokens;
                _completionTokens += result.CompletionTokens;
                _tokenSource = inferenceSource;
            }
            else if (string.Equals(inferenceSource, "web", StringComparison.OrdinalIgnoreCase))
            {
                _tokenSource = "web";
            }
        }
    }

    public void RecordToolCall(string toolName, long durationMs, bool error)
    {
        lock (_lock)
        {
            _toolCallCount++;
            if (error) _toolErrorCount++;
        }

        using var span = StartSpan("tool.execute", CurrentSpanId, new Dictionary<string, object?>
        {
            ["tool"] = toolName,
            ["error"] = error
        });
        span.SetAttribute("durationMs", durationMs);
    }

    public void RecordCompact(int tokensBefore, int tokensAfter)
    {
        using var span = StartSpan("context.compact", CurrentSpanId, new Dictionary<string, object?>
        {
            ["tokensBefore"] = tokensBefore,
            ["tokensAfter"] = tokensAfter
        });
    }

    public void RecordSandbox(string action, string detail)
    {
        using var span = StartSpan("sandbox." + action, CurrentSpanId, new Dictionary<string, object?>
        {
            ["detail"] = detail
        });
    }

    public void RecordEvalScore(string dataset, string caseId, double score, bool passed)
    {
        using var span = StartSpan("eval.score", CurrentSpanId, new Dictionary<string, object?>
        {
            ["dataset"] = dataset,
            ["caseId"] = caseId,
            ["score"] = score,
            ["passed"] = passed
        });
    }

    internal void EndSpan(string spanId, string status, Dictionary<string, object?>? extraAttributes)
    {
        if (!_openSpans.TryRemove(spanId, out var open))
            return;

        var span = new HarnessTraceSpan
        {
            TraceId = TraceId,
            SpanId = spanId,
            ParentSpanId = open.ParentSpanId,
            Name = open.Name,
            StartUtc = open.StartUtc.ToString("O"),
            EndUtc = DateTime.UtcNow.ToString("O"),
            DurationMs = open.Stopwatch.ElapsedMilliseconds,
            Status = status,
            Attributes = MergeAttributes(open.Attributes, extraAttributes)
        };

        AppendSpan(span);
    }

    public void FinalizeRun(HarnessRunMetaFinalizeArgs args)
    {
        if (_finalized) return;
        _finalized = true;

        if (RootSpanId is not null)
            EndSpan(RootSpanId, "ok", null);

        var meta = new HarnessRunMeta
        {
            RunId = RunId,
            TraceId = TraceId,
            StartedUtc = _startedUtc.ToString("O"),
            EndedUtc = DateTime.UtcNow.ToString("O"),
            DurationMs = _runSw.ElapsedMilliseconds,
            Model = _model ?? args.Model,
            Strategy = args.Strategy,
            SessionId = args.SessionId,
            PromptPreview = Truncate(args.PromptPreview, 300),
            AnswerPreview = Truncate(args.AnswerPreview, 500),
            PromptTokens = _promptTokens,
            CompletionTokens = _completionTokens,
            TotalTokens = _promptTokens + _completionTokens,
            TokenSource = _tokenSource,
            ToolCallCount = _toolCallCount,
            ToolErrorCount = _toolErrorCount,
            LlmCallCount = _llmCallCount,
            Phase = args.Phase,
            DomainId = args.DomainId
        };

        var metaPath = Path.Combine(_runDir, "meta.json");
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, JsonOptions));

        using var finalizeSpan = StartSpan("run.finalize", null, new Dictionary<string, object?>
        {
            ["postmortem"] = args.WrotePostMortem
        });

        if (args.RetentionDays > 0)
            HarnessRunTraceStore.PruneOldRuns(args.WorkspaceRoot, args.RetentionDays);

        if (_config is not null && HarnessLangfuseExporter.IsConfigured(_config))
            _ = Task.Run(() => HarnessLangfuseExporter.TryExportAsync(_runDir, _config));
    }

    public void Dispose()
    {
        if (!_finalized && RootSpanId is not null)
            EndSpan(RootSpanId, "cancelled", null);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, OpenSpan> _openSpans = new();

    private string StartSpanInternal(string name, string? parentSpanId, IReadOnlyDictionary<string, object?>? attributes)
    {
        var spanId = "span-" + Guid.NewGuid().ToString("N")[..10];
        _openSpans[spanId] = new OpenSpan
        {
            Name = name,
            ParentSpanId = parentSpanId,
            StartUtc = DateTime.UtcNow,
            Stopwatch = Stopwatch.StartNew(),
            Attributes = attributes is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(attributes)
        };
        return spanId;
    }

    private void AppendSpan(HarnessTraceSpan span)
    {
        var line = JsonSerializer.Serialize(span, JsonOptions);
        lock (_lock)
        {
            File.AppendAllText(_tracePath, line + Environment.NewLine);
        }
        AgentDebugLogger.Current?.Write("TRACE", $"{span.Name} {span.DurationMs}ms status={span.Status}");
        if (_config?.AgentLangfuseLiveExport == true && HarnessLangfuseExporter.IsConfigured(_config))
            _ = Task.Run(() => HarnessLangfuseExporter.TryExportSpanLiveAsync(span, RunId, TraceId, _config!));
    }

    private static Dictionary<string, object?> MergeAttributes(
        Dictionary<string, object?> baseline,
        Dictionary<string, object?>? extra)
    {
        if (extra is null || extra.Count == 0)
            return baseline;
        foreach (var kv in extra)
            baseline[kv.Key] = kv.Value;
        return baseline;
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    private sealed class OpenSpan
    {
        public required string Name { get; init; }
        public string? ParentSpanId { get; init; }
        public required DateTime StartUtc { get; init; }
        public required Stopwatch Stopwatch { get; init; }
        public Dictionary<string, object?> Attributes { get; init; } = new();
    }
}

public sealed class HarnessActiveSpan : IDisposable
{
    private readonly HarnessRunTracer _tracer;
    private readonly string _spanId;
    private string _status = "ok";
    private Dictionary<string, object?>? _extra;

    internal HarnessActiveSpan(HarnessRunTracer tracer, string spanId)
    {
        _tracer = tracer;
        _spanId = spanId;
    }

    public void SetStatus(string status) => _status = status;

    public void SetAttribute(string key, object? value)
    {
        _extra ??= new Dictionary<string, object?>();
        _extra[key] = value;
    }

    public void Dispose() => _tracer.EndSpan(_spanId, _status, _extra);
}

public sealed class HarnessRunTracerContext
{
    public string? Strategy { get; init; }
    public string? SessionId { get; init; }
    public string? Model { get; init; }
    public string? PromptPreview { get; init; }
}

public sealed class HarnessRunMetaFinalizeArgs
{
    public required string WorkspaceRoot { get; init; }
    public string? Strategy { get; init; }
    public string? SessionId { get; init; }
    public string? Model { get; init; }
    public string? PromptPreview { get; init; }
    public string? AnswerPreview { get; init; }
    public string? Phase { get; init; }
    public string? DomainId { get; init; }
    public bool WrotePostMortem { get; init; }
    public int RetentionDays { get; init; }
}
