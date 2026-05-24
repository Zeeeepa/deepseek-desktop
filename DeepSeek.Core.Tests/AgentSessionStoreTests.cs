using DeepSeekBrowser.Models;
using DeepSeekBrowser.Services;

namespace DeepSeek.Core.Tests;

public sealed class AgentSessionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly AgentSessionStore _store;

    public AgentSessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "deepseek_agent_sessions", Guid.NewGuid().ToString("N"));
        _store = new AgentSessionStore(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void SaveLoadAndListMetas_roundTrip()
    {
        var session = new AgentSessionData
        {
            Id = "s_test1",
            Title = "高斯定理解释",
            Messages =
            [
                new AgentSessionMessage { Role = "user", Text = "解释高斯定理" },
                new AgentSessionMessage { Role = "assistant", Answer = "高斯定理…" }
            ]
        };

        _store.Save(session);
        var metas = _store.ListMetas();
        Assert.Single(metas);
        Assert.Equal("高斯定理解释", metas[0].Title);

        var loaded = _store.Load("s_test1");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Messages.Count);
        Assert.Equal("解释高斯定理", loaded.Messages[0].Text);
    }

    [Fact]
    public void RenamePinAndDelete_updateSession()
    {
        _store.Save(new AgentSessionData { Id = "s_a", Title = "旧标题", Messages = [new AgentSessionMessage { Role = "user", Text = "hi" }] });

        Assert.True(_store.Rename("s_a", "新标题"));
        Assert.True(_store.SetPinned("s_a", true));

        var loaded = _store.Load("s_a");
        Assert.NotNull(loaded);
        Assert.Equal("新标题", loaded!.Title);
        Assert.True(loaded.Pinned);

        Assert.True(_store.Delete("s_a"));
        Assert.Null(_store.Load("s_a"));
    }
}
