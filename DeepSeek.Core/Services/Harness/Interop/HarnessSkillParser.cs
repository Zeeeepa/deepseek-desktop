using System.Text.RegularExpressions;

namespace DeepSeekBrowser.Services.Harness.Interop;

public static class HarnessSkillParser
{
    private static readonly Regex FrontmatterRegex = new(
        @"^---\s*\r?\n(.*?)\r?\n---\s*\r?\n(.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public static HarnessSkill ParseFile(string path, string source)
    {
        var text = File.ReadAllText(path);
        var dirName = new DirectoryInfo(Path.GetDirectoryName(path) ?? ".").Name;

        string? name = null;
        string? description = null;
        var body = text;

        var match = FrontmatterRegex.Match(text);
        if (match.Success)
        {
            ParseFrontmatter(match.Groups[1].Value, out name, out description);
            body = match.Groups[2].Value.Trim();
        }

        var id = SanitizeId(name ?? dirName);
        return new HarnessSkill
        {
            Id = id,
            Name = name ?? dirName,
            Description = description,
            Body = body,
            Source = source,
            FilePath = path
        };
    }

    private static void ParseFrontmatter(string yaml, out string? name, out string? description)
    {
        name = null;
        description = null;
        var descLines = new List<string>();
        var inDescription = false;

        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                name = Unquote(line["name:".Length..].Trim());
                inDescription = false;
                continue;
            }

            if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line["description:".Length..].Trim();
                if (rest is "|" or ">")
                {
                    inDescription = true;
                    continue;
                }

                description = Unquote(rest);
                inDescription = false;
                continue;
            }

            if (inDescription)
                descLines.Add(line.Trim());
        }

        if (descLines.Count > 0)
            description = string.Join(" ", descLines).Trim();
    }

    private static string? Unquote(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
            return value[1..^1];
        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
            return value[1..^1];
        return value;
    }

    private static string SanitizeId(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
}
