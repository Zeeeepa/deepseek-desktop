using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekBrowser.Services.Harness;

public static class HarnessPlaybookParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static HarnessPlaybook ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".yaml" or ".yml" ? ParseYaml(text) : ParseJson(text);
    }

    public static HarnessPlaybook ParseJson(string json)
    {
        var pb = JsonSerializer.Deserialize<HarnessPlaybookDto>(json, JsonOptions)
                 ?? throw new InvalidDataException("Playbook JSON 为空");
        return ToModel(pb);
    }

    public static HarnessPlaybook ParseYaml(string yaml)
    {
        var root = SimpleYamlReader.ReadDocument(yaml);
        var dto = new HarnessPlaybookDto
        {
            Id = GetString(root, "id"),
            Name = GetString(root, "name"),
            Description = GetString(root, "description"),
            Strategy = GetString(root, "strategy"),
            SystemAppend = GetString(root, "system_append") ?? GetString(root, "systemAppend"),
            Steps = GetStringList(root, "steps")
        };

        dto.Verify = ParseVerifyYaml(yaml, root);
        return ToModel(dto);
    }

    private static HarnessPlaybookVerifyDto? ParseVerifyYaml(string yaml, Dictionary<string, object?> root)
    {
        var steps = ParseVerifyStepsFromYamlLines(yaml);
        if (steps.Count > 0)
        {
            return new HarnessPlaybookVerifyDto
            {
                Steps = steps
            };
        }

        if (root.TryGetValue("verify", out var verifyNode) && verifyNode is Dictionary<string, object?> verifyMap)
        {
            return new HarnessPlaybookVerifyDto
            {
                Command = GetString(verifyMap, "command") ?? "",
                TimeoutSeconds = GetInt(verifyMap, "timeout_seconds") ?? GetInt(verifyMap, "timeoutSeconds") ?? 120,
                Optional = GetBool(verifyMap, "optional") ?? false
            };
        }

        return null;
    }

    private static List<HarnessVerifyStepDto> ParseVerifyStepsFromYamlLines(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var inVerify = false;
        var inSteps = false;
        HarnessVerifyStepDto? current = null;
        var list = new List<HarnessVerifyStepDto>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;

            if (!inVerify)
            {
                if (line.TrimStart().Equals("verify:", StringComparison.OrdinalIgnoreCase))
                    inVerify = true;
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("steps:", StringComparison.OrdinalIgnoreCase))
            {
                inSteps = true;
                continue;
            }

            if (inSteps && trimmed.StartsWith("- "))
            {
                if (current is not null)
                    list.Add(current);
                current = new HarnessVerifyStepDto();
                var rest = trimmed[2..].Trim();
                if (rest.StartsWith("command:", StringComparison.OrdinalIgnoreCase))
                    current.Command = rest["command:".Length..].Trim().Trim('"');
                continue;
            }

            if (inSteps && current is not null && trimmed.Contains(':'))
            {
                var idx = trimmed.IndexOf(':');
                var key = trimmed[..idx].Trim().ToLowerInvariant();
                var val = trimmed[(idx + 1)..].Trim();
                switch (key)
                {
                    case "command":
                        current.Command = val.Trim('"');
                        break;
                    case "optional":
                        current.Optional = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "timeout_seconds":
                    case "timeoutseconds":
                        if (int.TryParse(val, out var n))
                            current.TimeoutSeconds = n;
                        break;
                    case "name":
                        current.Name = val.Trim('"');
                        break;
                }
                continue;
            }

            if (inVerify && !inSteps && !trimmed.StartsWith('-') && trimmed.Contains(':'))
            {
                inSteps = false;
                if (current is not null)
                {
                    list.Add(current);
                    current = null;
                }
            }
        }

        if (current is not null)
            list.Add(current);

        return list.Where(s => !string.IsNullOrWhiteSpace(s.Command)).ToList();
    }

    private static HarnessPlaybook ToModel(HarnessPlaybookDto dto)
    {
        var id = (dto.Id ?? "").Trim();
        if (string.IsNullOrEmpty(id))
            throw new InvalidDataException("Playbook 缺少 id");

        HarnessPlaybookVerify? verify = null;
        if (dto.Verify is not null)
        {
            verify = new HarnessPlaybookVerify
            {
                Command = (dto.Verify.Command ?? "").Trim(),
                TimeoutSeconds = Math.Clamp(dto.Verify.TimeoutSeconds, 5, 600),
                Optional = dto.Verify.Optional,
                Steps = dto.Verify.Steps?
                    .Where(s => !string.IsNullOrWhiteSpace(s.Command))
                    .Select(s => new HarnessVerifyStep
                    {
                        Command = s.Command.Trim(),
                        Name = s.Name?.Trim(),
                        Optional = s.Optional,
                        TimeoutSeconds = Math.Clamp(s.TimeoutSeconds, 5, 600)
                    }).ToList() ?? []
            };

            if (verify.Steps.Count == 0 && string.IsNullOrWhiteSpace(verify.Command))
                verify = null;
        }

        return new HarnessPlaybook
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(dto.Name) ? id : dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Strategy = string.IsNullOrWhiteSpace(dto.Strategy) ? null : dto.Strategy.Trim(),
            SystemAppend = dto.SystemAppend?.Trim(),
            Steps = dto.Steps?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [],
            Verify = verify
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) ? v?.ToString() : null;

    private static int? GetInt(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is int i ? i : int.TryParse(v?.ToString(), out var n) ? n : null;

    private static bool? GetBool(IReadOnlyDictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out var v) && v is bool b ? b : bool.TryParse(v?.ToString(), out var x) ? x : null;

    private static List<string> GetStringList(IReadOnlyDictionary<string, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var v) || v is not List<object?> list)
            return [];
        return list.Select(x => x?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;
    }

    private sealed class HarnessPlaybookDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Strategy { get; set; }

        [JsonPropertyName("system_append")]
        public string? SystemAppend { get; set; }

        public List<string>? Steps { get; set; }
        public HarnessPlaybookVerifyDto? Verify { get; set; }
    }

    private sealed class HarnessPlaybookVerifyDto
    {
        public string Command { get; set; } = "";

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 120;

        public bool Optional { get; set; }
        public List<HarnessVerifyStepDto>? Steps { get; set; }
    }

    private sealed class HarnessVerifyStepDto
    {
        public string Command { get; set; } = "";
        public string? Name { get; set; }

        [JsonPropertyName("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 120;

        public bool Optional { get; set; }
    }
}
