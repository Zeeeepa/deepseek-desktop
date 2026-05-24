using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class HarnessSelfValidatorTests
{
    [Fact]
    public void ValidateBlueprint_passes_structured_answer()
    {
        const string answer = """
            ## 目标
            调研仓库
            ## 现状摘要
            ok
            ## 建议步骤
            1. 步骤
            ## 风险与依赖
            无
            ## 验收标准
            通过
            """;

        var result = HarnessSelfValidator.ValidateBlueprint(answer);
        Assert.True(result.Passed);
    }

    [Fact]
    public void ValidateBlueprint_fails_empty()
    {
        var result = HarnessSelfValidator.ValidateBlueprint("太短");
        Assert.False(result.Passed);
        Assert.NotEmpty(result.Issues);
    }
}

public sealed class HarnessDomainRouterTests
{
    [Fact]
    public void Route_coding_keywords()
    {
        var match = HarnessDomainRouter.Route("帮我 debug 这段 csharp 代码", null);
        Assert.Equal("coding", match.Id);
    }

    [Fact]
    public void Route_defaults_general()
    {
        var match = HarnessDomainRouter.Route("你好", null);
        Assert.Equal("general", match.Id);
    }
}

public sealed class HarnessMemoryLoaderTests
{
    [Fact]
    public void Load_includes_checkpoint_when_present()
    {
        var marker = "test-summary-" + Guid.NewGuid().ToString("N");
        HarnessCheckpointStore.Save(new HarnessCheckpoint { Summary = marker });

        var ctx = HarnessMemoryLoader.Load("分析代码", null);
        Assert.Equal(marker, ctx.CheckpointSummary);
    }
}
