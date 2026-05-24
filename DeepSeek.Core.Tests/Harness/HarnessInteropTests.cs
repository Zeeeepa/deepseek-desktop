using DeepSeekBrowser.Services.Harness.Interop;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessSkillParserTests
{
    [Fact]
    public void ParseFile_reads_frontmatter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "skill-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "SKILL.md");
        File.WriteAllText(path, """
            ---
            name: demo-skill
            description: Demo skill for interop
            ---
            # Body
            Do the thing.
            """);

        var skill = HarnessSkillParser.ParseFile(path, "test");

        Assert.Equal("demo-skill", skill.Id);
        Assert.Equal("Demo skill for interop", skill.Description);
        Assert.Contains("Do the thing", skill.Body);
    }
}

public sealed class HarnessMarketToolSchemaTests
{
    [Fact]
    public void BuildOpenAiBuiltinToolsJson_contains_read_file()
    {
        var json = HarnessMarketToolSchema.BuildOpenAiBuiltinToolsJson();
        Assert.Contains("read_file", json);
        Assert.Contains("\"type\": \"function\"", json);
    }
}
