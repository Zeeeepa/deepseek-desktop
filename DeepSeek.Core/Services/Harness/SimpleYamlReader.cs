namespace DeepSeekBrowser.Services.Harness;

/// <summary>
/// 极简 YAML 读取器，仅支持 DSD Playbook 扁平/二级结构（避免引入 YamlDotNet）。
/// </summary>
internal static class SimpleYamlReader
{
    public static Dictionary<string, object?> ReadDocument(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string? listKey = null;
        var listItems = new List<object?>();
        string? blockKey = null;
        var blockLines = new List<string>();
        string? nestedKey = null;
        var nestedMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        void FlushList()
        {
            if (listKey is null) return;
            root[listKey] = listItems.ToList();
            listKey = null;
            listItems.Clear();
        }

        void FlushBlock()
        {
            if (blockKey is null) return;
            root[blockKey] = string.Join("\n", blockLines).TrimEnd();
            blockKey = null;
            blockLines.Clear();
        }

        void FlushNested()
        {
            if (nestedKey is null) return;
            root[nestedKey] = nestedMap;
            nestedKey = null;
            nestedMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (blockKey is not null)
                    blockLines.Add("");
                continue;
            }

            if (raw.TrimStart().StartsWith('#'))
                continue;

            if (blockKey is not null)
            {
                if (raw.StartsWith(' ') || raw.StartsWith('\t'))
                {
                    blockLines.Add(raw.TrimStart());
                    continue;
                }

                FlushBlock();
            }

            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                listItems.Add(trimmed[2..].Trim());
                continue;
            }

            var idx = raw.IndexOf(':');
            if (idx <= 0) continue;

            var indent = raw.Length - raw.TrimStart().Length;
            var key = raw.TrimStart()[..(raw.TrimStart().IndexOf(':'))].Trim();
            var value = raw[(idx + 1)..].Trim();

            if (indent >= 2 && nestedKey is not null)
            {
                nestedMap[key] = ParseScalar(value);
                continue;
            }

            FlushList();
            FlushNested();

            if (string.IsNullOrEmpty(value))
            {
                if (key.Equals("verify", StringComparison.OrdinalIgnoreCase))
                {
                    nestedKey = key;
                    continue;
                }

                listKey = key;
                continue;
            }

            if (value is "|" or ">")
            {
                blockKey = key;
                continue;
            }

            root[key] = ParseScalar(value);
        }

        FlushBlock();
        FlushList();
        FlushNested();
        return root;
    }

    private static object? ParseScalar(string value)
    {
        if (value is "true" or "false")
            return value == "true";
        if (int.TryParse(value, out var n))
            return n;
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1].Replace("\\\"", "\"");
        return value;
    }
}
