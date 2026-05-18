using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Files.Results;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class FsReadFileToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_File_Text_Content()
    {
        var fileSystem = Substitute.For<IFileSystemService>();
        var logger = Substitute.For<ILogger<FsReadFileToolHandler>>();

        fileSystem.ReadTextAsync(Arg.Any<ReadFileTextCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<FileTextResult>>(new FileTextResult("workspace/readme.txt", "hello world")));

        var handler = new FsReadFileToolHandler(fileSystem, logger);
        var result = await handler.Handle(new FsReadFileRequest("readme.txt", "utf-8"), CancellationToken.None);

        Assert.True(result.IsSucc);
        var value = result.Match(
            Succ: item => item,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.False(value.IsError);
        Assert.Single(value.Content);
        Assert.Equal("hello world", value.Content[0].Text);
        Assert.IsType<FileTextResult>(value.StructuredContent);
    }
}
