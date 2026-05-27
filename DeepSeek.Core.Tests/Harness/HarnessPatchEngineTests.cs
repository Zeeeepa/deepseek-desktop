using DeepSeekBrowser.Services.Harness;

namespace DeepSeekBrowser.Tests.Harness;

public class HarnessPatchEngineTests
{
    [Fact]
    public void Apply_ExactMatch_ReplacesOnce()
    {
        var content = "alpha\nbeta\ngamma\n";
        var result = HarnessPatchEngine.Apply(new HarnessPatchRequest
        {
            FilePath = "a.txt",
            OldString = "beta",
            NewString = "BETA",
            ReplaceAll = false
        }, content);

        Assert.True(result.Success);
        Assert.Equal("alpha\nBETA\ngamma\n", result.PatchedContent);
        Assert.Equal(1, result.MatchCount);
    }

    [Fact]
    public void Apply_ReplaceAll_ReplacesAll()
    {
        var content = "foo bar foo baz foo";
        var result = HarnessPatchEngine.Apply(new HarnessPatchRequest
        {
            FilePath = "a.txt",
            OldString = "foo",
            NewString = "qux",
            ReplaceAll = true
        }, content);

        Assert.True(result.Success);
        Assert.Equal("qux bar qux baz qux", result.PatchedContent);
        Assert.Equal(3, result.MatchCount);
    }

    [Fact]
    public void Apply_MissingOldString_FailsWithHint()
    {
        var content = "line one\nline two\n";
        var result = HarnessPatchEngine.Apply(new HarnessPatchRequest
        {
            FilePath = "a.txt",
            OldString = "line three",
            NewString = "x",
            ReplaceAll = false
        }, content);

        Assert.False(result.Success);
        Assert.Contains("未在文件中找到", result.Error);
    }

    [Fact]
    public void Apply_MultipleWithoutReplaceAll_Fails()
    {
        var content = "dup\ndup\n";
        var result = HarnessPatchEngine.Apply(new HarnessPatchRequest
        {
            FilePath = "a.txt",
            OldString = "dup",
            NewString = "x",
            ReplaceAll = false
        }, content);

        Assert.False(result.Success);
        Assert.Contains("出现 2 次", result.Error);
    }

    [Fact]
    public void BuildUnifiedDiff_ContainsPlusMinus()
    {
        var diff = HarnessPatchEngine.BuildUnifiedDiff("f.cs", "a\nb\n", "a\nB\n");
        Assert.Contains("-b", diff);
        Assert.Contains("+B", diff);
    }
}
