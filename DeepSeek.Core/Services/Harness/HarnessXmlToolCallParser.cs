using System.Text.Json;
using System.Text.RegularExpressions;
using DeepSeekBrowser.Services;

namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 解析 Web 推理模式下模型输出的 &lt;tool_calling&gt; XML，以及常见的伪工具调用文本。
/// </summary>
public static class HarnessXmlToolCallParser
{
    private static readonly JsonSerializerOptions ToolJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex XmlBlock = new(
        @"<tool_calling>\s*<name>\s*(?<name>[^<]+?)\s*</name>\s*<arguments>\s*(?<args>[\s\S]*?)\s*</arguments>\s*</tool_calling>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 模型偶发输出：50129e5e__list_directory{"path":"..."} 或 list_dir {"path":"."}
    private static readonly Regex LooseInline = new(
        @"(?:^|\n|\s)(?:[0-9a-f]{6,}__)?(?<name>list_directory|list_dir|write_file|write|read_file|read|grep|glob|run_shell|bash)\s*(?<args>\{[\s\S]*?\})(?=\s|$|\n)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ToolCallBlock = new(
        @"(?s)\[TOOL_CALL\]\s*(?<inner>.*?)\s*\[/TOOL_CALL\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex XmlToolCallBlock = new(
        @"(?s)<(?:deepseek:)?tool_call[^>]*>\s*(?<inner>.*?)\s*</(?:deepseek:)?tool_call>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InvokeBlock = new(
        @"(?s)<invoke\s+name\s*=\s*""(?<name>[^""]+)""[^>]*>(?<inner>.*?)</invoke>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CallLine = new(
        @"(?:^|\n)\s*Call:\s*`?(?<name>(?:[0-9a-f]{6,}__)?(?:write_file|write|read_file|read|list_dir|list_directory|grep|glob|run_shell|bash))`?\s*(?<rest>[\s\S]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsonFenceBlock = new(
        @"```(?:json)?\s*([\s\S]*?)```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OpenJsonFenceTail = new(
        @"```json[\s\S]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool HasToolCallMarkers(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;
        return content.Contains("[TOOL_CALL]", StringComparison.OrdinalIgnoreCase)
               || content.Contains("<tool_calling>", StringComparison.OrdinalIgnoreCase)
               || content.Contains("<tool_call", StringComparison.OrdinalIgnoreCase)
               || content.Contains("<invoke ", StringComparison.OrdinalIgnoreCase)
               || content.Contains("```json", StringComparison.OrdinalIgnoreCase)
               || CallLine.IsMatch(content)
               || LooseInline.IsMatch(content)
               || ContainsLooseJsonToolObject(content);
    }

    /// <summary>空/未闭合的 ```json 围栏（模型常只输出计划不调用工具）。</summary>
    public static bool HasIncompleteToolArtifacts(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (Regex.IsMatch(content, @"```json\s*```", RegexOptions.IgnoreCase))
            return true;

        if (!content.Contains("```json", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (Match m in JsonFenceBlock.Matches(content))
        {
            var body = m.Groups[1].Value.Trim();
            if (body.Length == 0)
                return true;
            if (body.StartsWith('{') && !body.EndsWith('}'))
                return true;
        }

        if (OpenJsonFenceTail.IsMatch(content) && !JsonFenceBlock.IsMatch(content))
            return true;

        return false;
    }

    public static IReadOnlyList<WebToolCall> TryParse(string? content, out string strippedContent)
    {
        strippedContent = content ?? "";
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<WebToolCall>();

        var calls = new List<WebToolCall>();
        var sb = new System.Text.StringBuilder();
        var last = 0;
        var id = 0;

        foreach (Match m in XmlBlock.Matches(content))
        {
            sb.Append(content, last, m.Index - last);
            last = m.Index + m.Length;
            var name = NormalizeToolName(m.Groups["name"].Value);
            var args = NormalizeWriteFileArguments(name, m.Groups["args"].Value.Trim());
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(args))
                continue;
            calls.Add(new WebToolCall
            {
                Id = "xml-" + ++id,
                Name = name,
                Arguments = args
            });
        }

        sb.Append(content, last, content.Length - last);
        strippedContent = sb.ToString().Trim();

        if (calls.Count > 0)
            return calls;

        foreach (Match m in ToolCallBlock.Matches(content))
        {
            if (!TryParseToolCallInner(m.Groups["inner"].Value, ref id, calls))
                continue;
            strippedContent = ToolCallBlock.Replace(strippedContent, "").Trim();
        }

        if (calls.Count > 0)
            return calls;

        foreach (Match m in XmlToolCallBlock.Matches(content))
        {
            var inner = m.Groups["inner"].Value;
            if (TryParseInvokeInner(inner, ref id, calls))
                strippedContent = XmlToolCallBlock.Replace(strippedContent, "").Trim();
            else if (TryParseToolCallInner(inner, ref id, calls))
                strippedContent = XmlToolCallBlock.Replace(strippedContent, "").Trim();
        }

        if (calls.Count > 0)
            return calls;

        foreach (Match m in InvokeBlock.Matches(content))
        {
            var name = NormalizeToolName(m.Groups["name"].Value);
            var args = ParseXmlParameters(m.Groups["inner"].Value);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            calls.Add(new WebToolCall { Id = "invoke-" + ++id, Name = name, Arguments = args });
            strippedContent = InvokeBlock.Replace(strippedContent, "").Trim();
        }

        if (calls.Count > 0)
            return calls;

        foreach (Match m in CallLine.Matches(content))
        {
            var name = NormalizeToolName(m.Groups["name"].Value);
            var rest = m.Groups["rest"].Value;
            var args = TryExtractArgsFromRest(rest, name);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(args))
                continue;
            calls.Add(new WebToolCall { Id = "call-" + ++id, Name = name, Arguments = args });
            strippedContent = CallLine.Replace(strippedContent, "").Trim();
        }

        if (calls.Count > 0)
            return calls;

        foreach (Match m in LooseInline.Matches(content))
        {
            var name = NormalizeToolName(m.Groups["name"].Value);
            var args = m.Groups["args"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            calls.Add(new WebToolCall
            {
                Id = "loose-" + ++id,
                Name = name,
                Arguments = args
            });
        }

        if (calls.Count > 0)
        {
            strippedContent = LooseInline.Replace(content, "").Trim();
            return calls;
        }

        if (TryParseJsonFences(content, ref id, calls))
        {
            strippedContent = JsonFenceBlock.Replace(content, "").Trim();
            return calls;
        }

        if (TryParseLooseJsonToolObjects(content, ref id, calls))
        {
            strippedContent = StripLooseJsonToolObjects(content).Trim();
            return calls;
        }

        return calls;
    }

    /// <summary>模型偶发在正文输出单行 JSON 工具调用（非 XML / 非围栏）。</summary>
    public static bool ContainsLooseJsonToolObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '{')
                continue;
            var end = FindBalancedJsonEnd(content, i);
            if (end < 0)
                continue;
            var slice = content[i..(end + 1)];
            if (LooksLikeToolJsonObject(slice))
                return true;
        }

        return false;
    }

    public static string StripLooseJsonToolObjects(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content ?? "";

        var sb = new System.Text.StringBuilder();
        var last = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '{')
                continue;
            var end = FindBalancedJsonEnd(content, i);
            if (end < 0)
                continue;
            var slice = content[i..(end + 1)];
            if (!LooksLikeToolJsonObject(slice))
                continue;
            sb.Append(content, last, i - last);
            last = end + 1;
            i = end;
        }

        if (last == 0)
            return content;

        sb.Append(content, last, content.Length - last);
        return sb.ToString();
    }

    private static bool TryParseLooseJsonToolObjects(string content, ref int id, List<WebToolCall> calls)
    {
        var any = false;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '{')
                continue;
            var end = FindBalancedJsonEnd(content, i);
            if (end < 0)
                continue;
            var slice = content[i..(end + 1)];
            if (!LooksLikeToolJsonObject(slice))
                continue;
            if (!TryParseJsonToolBody(slice, ref id, calls))
                continue;
            any = true;
        }

        return any;
    }

    private static bool LooksLikeToolJsonObject(string slice)
    {
        if (!slice.Contains("\"name\"", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!slice.Contains("\"arguments\"", StringComparison.OrdinalIgnoreCase)
            && !slice.Contains("\"args\"", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(slice);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && (doc.RootElement.TryGetProperty("name", out _)
                       || doc.RootElement.TryGetProperty("tool", out _)
                       || doc.RootElement.TryGetProperty("function", out _));
        }
        catch
        {
            return false;
        }
    }

    private static int FindBalancedJsonEnd(string s, int start)
    {
        if (start >= s.Length || s[start] != '{')
            return -1;

        var depth = 0;
        var inStr = false;
        var esc = false;
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (inStr)
            {
                if (esc) esc = false;
                else if (c == '\\') esc = true;
                else if (c == '"') inStr = false;
                continue;
            }

            if (c == '"')
            {
                inStr = true;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool TryParseJsonFences(string content, ref int id, List<WebToolCall> calls)
    {
        var any = false;
        foreach (Match m in JsonFenceBlock.Matches(content))
        {
            var body = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(body) || !body.StartsWith('{'))
                continue;
            if (!TryParseJsonToolBody(body, ref id, calls))
                continue;
            any = true;
        }

        return any;
    }

    private static bool TryParseJsonToolBody(string body, ref int id, List<WebToolCall> calls)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                var ok = false;
                foreach (var item in root.EnumerateArray())
                {
                    if (TryParseJsonToolElement(item, ref id, calls))
                        ok = true;
                }

                return ok;
            }

            return TryParseJsonToolElement(root, ref id, calls);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseJsonToolElement(JsonElement el, ref int id, List<WebToolCall> calls)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return false;

        if (el.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
            return TryParseJsonToolElement(fn, ref id, calls);

        var name = GetJsonString(el, "name")
                   ?? GetJsonString(el, "tool")
                   ?? GetJsonString(el, "function");
        if (string.IsNullOrWhiteSpace(name))
        {
            if (el.TryGetProperty("content", out _))
                name = "write_file";
            else if (el.TryGetProperty("file_path", out _) || el.TryGetProperty("path", out _))
                name = "list_dir";
            else
                return false;
        }

        string argsJson;
        if (el.TryGetProperty("arguments", out var argsEl))
            argsJson = argsEl.ValueKind == JsonValueKind.String
                ? argsEl.GetString() ?? "{}"
                : argsEl.GetRawText();
        else if (el.TryGetProperty("args", out var args2))
            argsJson = args2.ValueKind == JsonValueKind.String
                ? args2.GetString() ?? "{}"
                : args2.GetRawText();
        else if (el.TryGetProperty("input", out var input))
            argsJson = input.GetRawText();
        else
            argsJson = el.ValueKind == JsonValueKind.Object ? el.GetRawText() : "{}";

        if (argsJson.StartsWith('{') == false && argsJson.StartsWith('[') == false)
        {
            try
            {
                argsJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["value"] = argsJson });
            }
            catch
            {
                argsJson = "{}";
            }
        }

        var norm = NormalizeToolName(name);
        argsJson = NormalizeWriteFileArguments(norm, argsJson);
        calls.Add(new WebToolCall
        {
            Id = "json-" + ++id,
            Name = norm,
            Arguments = argsJson
        });
        return true;
    }

    public static string NormalizeToolName(string? raw)
    {
        var n = (raw ?? "").Trim();
        if (n.Length == 0)
            return n;

        var idx = n.IndexOf("__", StringComparison.Ordinal);
        if (idx >= 0)
            n = n[(idx + 2)..];

        return n.ToLowerInvariant() switch
        {
            "list_directory" => "list_dir",
            "write" => "write_file",
            "read" => "read_file",
            "bash" => "run_shell",
            _ => n
        };
    }

    private static bool TryParseToolCallInner(string inner, ref int id, List<WebToolCall> calls)
    {
        inner = inner.Trim();
        if (inner.Length == 0)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(inner);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var name = GetJsonString(doc.RootElement, "tool")
                           ?? GetJsonString(doc.RootElement, "name")
                           ?? GetJsonString(doc.RootElement, "function");
                var argsEl = doc.RootElement.TryGetProperty("args", out var a) ? a
                    : doc.RootElement.TryGetProperty("arguments", out var b) ? b
                    : doc.RootElement.TryGetProperty("input", out var c) ? c
                    : default;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    calls.Add(new WebToolCall
                    {
                        Id = "tc-" + ++id,
                        Name = NormalizeToolName(name),
                        Arguments = argsEl.ValueKind != JsonValueKind.Undefined
                            ? argsEl.GetRawText()
                            : "{}"
                    });
                    return true;
                }
            }
        }
        catch
        {
            // not json
        }

        var arrow = Regex.Match(inner,
            @"tool\s*=>\s*""(?<tool>[^""]+)""\s*,\s*args\s*=>\s*(?<args>\{[\s\S]*\})",
            RegexOptions.IgnoreCase);
        if (arrow.Success)
        {
            calls.Add(new WebToolCall
            {
                Id = "arrow-" + ++id,
                Name = NormalizeToolName(arrow.Groups["tool"].Value),
                Arguments = arrow.Groups["args"].Value.Trim()
            });
            return true;
        }

        return false;
    }

    private static bool TryParseInvokeInner(string inner, ref int id, List<WebToolCall> calls)
    {
        foreach (Match m in InvokeBlock.Matches(inner))
        {
            var name = NormalizeToolName(m.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            calls.Add(new WebToolCall
            {
                Id = "invoke-" + ++id,
                Name = name,
                Arguments = ParseXmlParameters(m.Groups["inner"].Value)
            });
            return true;
        }

        return false;
    }

    private static string ParseXmlParameters(string inner)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paramRe = new Regex(
            @"<(?:parameter|param)\s+name\s*=\s*""([^""]+)""[^>]*>([\s\S]*?)</(?:parameter|param)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (Match m in paramRe.Matches(inner))
        {
            var key = m.Groups[1].Value.Trim();
            var val = m.Groups[2].Value.Trim();
            if (key.Length > 0)
                map[key] = val;
        }

        if (map.Count == 0)
            return "{}";

        return JsonSerializer.Serialize(map);
    }

    private static string? TryExtractArgsFromRest(string rest, string toolName)
    {
        var json = Regex.Match(rest, @"(\{[\s\S]*?\})");
        if (json.Success)
            return json.Groups[1].Value;

        var fence = Regex.Match(rest, @"```(?:\w+)?\s*([\s\S]*?)```");
        if (!fence.Success)
            return null;

        var body = fence.Groups[1].Value.Trim();
        if (body.Length == 0)
            return null;

        var norm = NormalizeToolName(toolName);
        if (norm is "write_file" or "write")
        {
            var pathMatch = Regex.Match(rest, @"```(\w+)", RegexOptions.IgnoreCase);
            var ext = pathMatch.Success ? pathMatch.Groups[1].Value.ToLowerInvariant() : "html";
            var file = ext switch
            {
                "html" or "htm" => "index.html",
                "js" => "snake.js",
                "css" => "snake.css",
                _ => "output." + ext
            };
            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["file_path"] = file,
                ["content"] = body
            });
        }

        return JsonSerializer.Serialize(new Dictionary<string, string> { ["content"] = body });
    }

    /// <summary>修复 write_file 参数：JSON 破损时从 ``` 围栏提取 content，path → file_path。</summary>
    public static string NormalizeWriteFileArguments(string toolName, string args)
    {
        var norm = NormalizeToolName(toolName);
        if (norm is not ("write_file" or "write"))
            return args;

        return RepairWriteFileArguments(args);
    }

    public static string RepairWriteFileArguments(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
            return args;

        var trimmed = args.Trim();
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            var path = GetJsonString(root, "file_path") ?? GetJsonString(root, "path");
            var content = GetJsonString(root, "content") ?? "";
            var body = content;
            if (string.IsNullOrWhiteSpace(body) || !Regex.IsMatch(body, @"\S"))
            {
                var fromFence = TryExtractBodyFromFence(trimmed);
                if (fromFence is not null)
                    body = fromFence;
            }
            else
            {
                var innerFence = TryExtractBodyFromFence(body);
                if (innerFence is not null && innerFence.Length > body.Trim().Length / 2)
                    body = innerFence;
            }

            if (string.IsNullOrWhiteSpace(path))
                return trimmed;
            if (string.IsNullOrWhiteSpace(body))
                return trimmed;

            if (GetJsonString(root, "file_path") == path && body == content)
                return trimmed;

            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["file_path"] = path,
                ["content"] = body
            }, ToolJsonOptions);
        }
        catch
        {
            var pathMatch = Regex.Match(trimmed, @"""(?:path|file_path)""\s*:\s*""([^""]+)""");
            var path = pathMatch.Success ? pathMatch.Groups[1].Value : "index.html";
            var body = TryExtractBodyFromFence(trimmed);
            if (body is null)
                return trimmed;
            return JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["file_path"] = path,
                ["content"] = body
            }, ToolJsonOptions);
        }
    }

    private static string? TryExtractBodyFromFence(string text)
    {
        var fence = Regex.Match(text, @"```(?:\w+)?\s*([\s\S]*?)```");
        if (!fence.Success || !Regex.IsMatch(fence.Groups[1].Value, @"\S"))
            return null;
        return fence.Groups[1].Value.Trim();
    }

    private static string? GetJsonString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
