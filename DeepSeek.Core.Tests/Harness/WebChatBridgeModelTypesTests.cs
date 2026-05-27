using DeepSeekBrowser.Services.Harness;

namespace DeepSeek.Core.Tests.Harness;

public sealed class WebChatBridgeModelTypesTests
{
    [Theory]
    [InlineData("deepseek-v4-flash", "default")]
    [InlineData("deepseek-v4-pro", "expert")]
    [InlineData("deepseek-reasoner", "expert")]
    public void Resolve_maps_model_family(string model, string expected) =>
        Assert.Equal(expected, WebChatBridgeModelTypes.Resolve(model));
}
