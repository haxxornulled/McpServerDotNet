using LanguageExt;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Protocol.Shared;
using McpServer.Protocol.Resources;
using McpServer.Protocol.Routing;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Protocol;

public sealed class ResourceReadRouterTests
{
    [Fact]
    public void ListResources_Should_Expose_Registered_Schemes()
    {
        var fileHandler = Substitute.For<IResourceHandler>();
        fileHandler.UriScheme.Returns("file");
        fileHandler.Name.Returns("file");
        fileHandler.Description.Returns("File text resource");
        fileHandler.Describe().Returns(new ResourceDescriptor(
            "file",
            "File text resource",
            "file:///workspace/example.txt",
            "File text resource",
            "text/plain"));

        var dirHandler = Substitute.For<IResourceHandler>();
        dirHandler.UriScheme.Returns("dir");
        dirHandler.Name.Returns("dir");
        dirHandler.Description.Returns("Directory listing resource");
        dirHandler.Describe().Returns(new ResourceDescriptor(
            "dir",
            "Directory listing resource",
            "dir:///workspace",
            "Directory listing resource",
            "application/json"));

        var router = new ResourceReadRouter([fileHandler, dirHandler]);

        var result = router.ListResources();

        Assert.Equal(2, result.Resources.Count);
        Assert.Contains(result.Resources, resource => resource.Name == "file" && resource.Uri == "file:///workspace/example.txt");
        Assert.Contains(result.Resources, resource => resource.Name == "dir" && resource.Uri == "dir:///workspace");
    }

    [Fact]
    public async Task RouteAsync_Should_Map_Text_Contents_To_Protocol_Dto()
    {
        var fileHandler = Substitute.For<IResourceHandler>();
        fileHandler.UriScheme.Returns("file");
        fileHandler.Name.Returns("file");
        fileHandler.Description.Returns("File text resource");
        fileHandler.Describe().Returns(new ResourceDescriptor(
            "file",
            "File text resource",
            "file:///workspace/example.txt",
            "File text resource",
            "text/plain"));
        fileHandler.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ReadResourceResult>>(
                new ReadResourceResult([new ResourceContent("file:///workspace/example.txt", "text/plain", text: "hello from resource")])));

        var router = new ResourceReadRouter([fileHandler]);

        var result = await router.RouteAsync("file:///workspace/example.txt", CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var content = Assert.Single(dto.Contents);
        var textContent = Assert.IsType<TextResourceContentsDto>(content);
        Assert.Equal("file:///workspace/example.txt", textContent.Uri);
        Assert.Equal("hello from resource", textContent.Text);
    }

    [Fact]
    public async Task RouteAsync_Should_Reject_Unknown_Schemes()
    {
        var router = new ResourceReadRouter(Array.Empty<IResourceHandler>());

        var result = await router.RouteAsync("file:///workspace/example.txt", CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected an error."),
            Fail: value => value);

        Assert.Contains("No resource handler for scheme", error.Message, StringComparison.Ordinal);
    }
}
