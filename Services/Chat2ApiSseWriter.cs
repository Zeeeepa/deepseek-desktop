using System.IO;
using System.Text;
using System.Text.Json;
using DeepSeekBrowser.Models;

namespace DeepSeekBrowser.Services;

/// <summary>OpenAI 兼容 SSE 流（对齐 Chat2API / OpenAI chat.completion.chunk）。</summary>
public static class Chat2ApiSseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>将网页真流式事件转为 OpenAI SSE。</summary>
    public static async Task PipeWebStreamAsync(
        Stream output,
        IAsyncEnumerable<WebChatStreamEvent> events,
        string model,
        CancellationToken ct = default)
    {
        var id = "chatcmpl-" + Guid.NewGuid().ToString("N")[..12];
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sentRole = false;
        var sentContent = false;

        await foreach (var ev in events.WithCancellation(ct))
        {
            switch (ev)
            {
                case WebChatStreamDelta delta when delta.Kind == WebChatStreamDelta.Reasoning:
                    if (!sentRole)
                    {
                        await WriteChunkAsync(output, id, created, model, new { role = "assistant" }, null, ct);
                        sentRole = true;
                    }
                    await WriteChunkAsync(output, id, created, model,
                        new { reasoning_content = delta.Text }, null, ct);
                    break;

                case WebChatStreamDelta delta:
                    if (!sentRole)
                    {
                        await WriteChunkAsync(output, id, created, model, new { role = "assistant" }, null, ct);
                        sentRole = true;
                    }
                    sentContent = true;
                    await WriteChunkAsync(output, id, created, model,
                        new { content = delta.Text }, null, ct);
                    break;

                case WebChatStreamDone done:
                    await WriteFinalChunksAsync(output, id, created, model, done.Result, sentRole, sentContent, ct);
                    await WriteRawAsync(output, "data: [DONE]\n\n", ct);
                    return;

                case WebChatStreamError err:
                    var errJson = JsonSerializer.Serialize(new
                    {
                        error = new { message = err.Message, type = "server_error" }
                    }, JsonOptions);
                    await WriteRawAsync(output, $"data: {errJson}\n\n", ct);
                    await WriteRawAsync(output, "data: [DONE]\n\n", ct);
                    return;
            }
        }

        await WriteRawAsync(output, "data: [DONE]\n\n", ct);
    }

    private static async Task WriteFinalChunksAsync(
        Stream output,
        string id,
        long created,
        string model,
        WebChatResult result,
        bool sentRole,
        bool sentContent,
        CancellationToken ct)
    {
        if (!sentRole)
            await WriteChunkAsync(output, id, created, model, new { role = "assistant" }, null, ct);

        if (!sentContent && !string.IsNullOrEmpty(result.Content))
            await WriteChunkAsync(output, id, created, model, new { content = result.Content }, null, ct);

        if (result.ToolCalls is { Count: > 0 })
        {
            var toolCalls = result.ToolCalls.Select(tc => new
            {
                index = 0,
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.Arguments }
            }).ToArray();
            await WriteChunkAsync(output, id, created, model,
                new { tool_calls = toolCalls }, null, ct);
            await WriteChunkAsync(output, id, created, model, new { }, "tool_calls", ct);
            return;
        }

        var finish = result.FinishReason ?? "stop";
        await WriteChunkAsync(output, id, created, model, new { }, finish, ct);
    }

    public static async Task WriteCompletionStreamAsync(
        Stream output,
        WebChatResult result,
        string model,
        CancellationToken ct = default)
    {
        var id = "chatcmpl-" + Guid.NewGuid().ToString("N")[..12];
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await WriteChunkAsync(output, id, created, model, new { role = "assistant" }, null, ct);

        if (!string.IsNullOrEmpty(result.ReasoningContent))
        {
            foreach (var piece in ChunkText(result.ReasoningContent, 24))
            {
                await WriteChunkAsync(output, id, created, model,
                    new { reasoning_content = piece }, null, ct);
            }
        }

        if (result.ToolCalls is { Count: > 0 })
        {
            var toolCalls = result.ToolCalls.Select(tc => new
            {
                index = 0,
                id = tc.Id,
                type = "function",
                function = new { name = tc.Name, arguments = tc.Arguments }
            }).ToArray();
            await WriteChunkAsync(output, id, created, model,
                new { tool_calls = toolCalls }, null, ct);
            await WriteChunkAsync(output, id, created, model, new { }, "tool_calls", ct);
        }
        else
        {
            var content = result.Content ?? "";
            foreach (var piece in ChunkText(content, 16))
            {
                await WriteChunkAsync(output, id, created, model,
                    new { content = piece }, null, ct);
            }

            var finish = result.FinishReason ?? "stop";
            await WriteChunkAsync(output, id, created, model, new { }, finish, ct);
        }

        await WriteRawAsync(output, "data: [DONE]\n\n", ct);
    }

    private static IEnumerable<string> ChunkText(string text, int size)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        for (var i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }

    private static async Task WriteChunkAsync(
        Stream output,
        string id,
        long created,
        string model,
        object delta,
        string? finishReason,
        CancellationToken ct)
    {
        var payload = new
        {
            id,
            @object = "chat.completion.chunk",
            created,
            model,
            choices = new[]
            {
                new
                {
                    index = 0,
                    delta,
                    finish_reason = finishReason
                }
            }
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await WriteRawAsync(output, $"data: {json}\n\n", ct);
    }

    private static async Task WriteRawAsync(Stream output, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await output.WriteAsync(bytes, ct);
        await output.FlushAsync(ct);
    }
}
