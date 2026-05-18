using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Routing;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Protocol;

public sealed class PromptRouterTests
{
    [Fact]
    public void ListPrompts_Should_Return_Typed_Result()
    {
        var handler = Substitute.For<IPromptHandler>();
        handler.Name.Returns("prompt.test");
        handler.Description.Returns("test prompt");
        handler.Describe().Returns(new PromptDescriptor(
            "prompt.test",
            "Test Prompt",
            "test prompt",
            [new PromptArgumentDescriptor("uri", "URI", "resource uri", true)]));

        var router = new PromptRouter([handler]);
        var result = router.ListPrompts();

        var prompt = Assert.Single(result.Prompts);
        Assert.Equal("prompt.test", prompt.Name);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Typed_Result()
    {
        var handler = Substitute.For<IPromptHandler>();
        handler.Name.Returns("prompt.test");
        handler.Description.Returns("test prompt");
        handler.Describe().Returns(new PromptDescriptor("prompt.test", "Test Prompt", "test prompt", null));
        handler.GetAsync(Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<GetPromptResult>>(
                new GetPromptResult("desc", [new PromptMessage("user", PromptMessageContent.FromText("hello"))])));

        var router = new PromptRouter([handler]);
        var args = JsonSerializer.SerializeToElement(new { uri = "file:///workspace/a.txt" });

        var result = await router.GetAsync("prompt.test", args, CancellationToken.None);

        Assert.True(result.IsSucc);
    }
}
