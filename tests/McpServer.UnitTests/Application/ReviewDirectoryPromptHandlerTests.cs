using System.Text.Json;
using McpServer.Application.Mcp.Prompts;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class ReviewDirectoryPromptHandlerTests
{
    [Fact]
    public async Task GetAsync_Should_Require_Grounded_File_Evidence()
    {
        var handler = new ReviewDirectoryPromptHandler();
        var arguments = JsonSerializer.SerializeToElement(new
        {
            uri = "dir:///workspace",
            goal = "find correctness bugs"
        });

        var result = await handler.GetAsync(arguments, CancellationToken.None);

        Assert.True(result.IsSucc);
        var prompt = result.Match(
            Succ: value => Assert.Single(value.Messages).Content.Text,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Contains("Start by calling workspace.inspect", prompt, StringComparison.Ordinal);
        Assert.Contains("Before reporting any finding, call fs.read_file", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not invent files, functions, line numbers", prompt, StringComparison.Ordinal);
        Assert.Contains("If no proven issues are found", prompt, StringComparison.Ordinal);
    }
}
