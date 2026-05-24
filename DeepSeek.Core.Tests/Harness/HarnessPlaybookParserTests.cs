using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessPlaybookParserTests
{
    [Fact]
    public void ParseYaml_reads_blueprint_playbook()
    {
        const string yaml = """
            id: blueprint-repo
            name: 仓库 Blueprint
            strategy: blueprint
            system_append: |
              优先阅读 README
            steps:
              - 浏览结构
              - 输出方案
            """;

        var pb = HarnessPlaybookParser.ParseYaml(yaml);

        Assert.Equal("blueprint-repo", pb.Id);
        Assert.Equal("blueprint", pb.Strategy);
        Assert.Contains("README", pb.SystemAppend);
        Assert.Equal(2, pb.Steps.Count);
    }

    [Fact]
    public void ParseYaml_reads_verify_block()
    {
        const string yaml = """
            id: execute-with-verify
            name: Execute + Verify
            strategy: execute
            verify:
              command: dotnet test
              timeout_seconds: 90
              optional: true
            """;

        var pb = HarnessPlaybookParser.ParseYaml(yaml);

        Assert.NotNull(pb.Verify);
        Assert.Equal("dotnet test", pb.Verify!.Command);
        Assert.Equal(90, pb.Verify.TimeoutSeconds);
        Assert.True(pb.Verify.Optional);
    }

    [Fact]
    public void ParseJson_reads_playbook()
    {
        const string json = """
            {
              "id": "json-pb",
              "name": "JSON Playbook",
              "strategy": "execute",
              "steps": ["a", "b"]
            }
            """;

        var pb = HarnessPlaybookParser.ParseJson(json);
        Assert.Equal("json-pb", pb.Id);
        Assert.Equal(2, pb.Steps.Count);
    }
}
