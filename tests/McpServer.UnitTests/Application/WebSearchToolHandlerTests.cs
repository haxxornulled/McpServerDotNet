using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WebSearchToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_Structured_Search_Summary()
    {
        var web = Substitute.For<IWebSearchService>();
        var logger = Substitute.For<ILogger<WebSearchToolHandler>>();

        web.SearchAsync(Arg.Any<McpServer.Application.Web.Commands.SearchWebCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<IReadOnlyList<WebSearchResult>>>(
                new[]
                {
                    new WebSearchResult("Result One", "https://example.com/1", "Snippet one", 0.99),
                    new WebSearchResult("Result Two", "https://example.com/2", "Snippet two", 0.75)
                }));

        var handler = new WebSearchToolHandler(web, logger);
        var result = await handler.Handle(new WebSearchRequest("hello world", 2), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = dto.StructuredContent ?? throw new InvalidOperationException("Expected structured content.");
        var queryProperty = structured.GetType().GetProperty("query");
        Assert.NotNull(queryProperty);
        Assert.Equal("hello world", queryProperty!.GetValue(structured));

        var resultCountProperty = structured.GetType().GetProperty("result_count");
        Assert.NotNull(resultCountProperty);
        Assert.Equal(2, resultCountProperty!.GetValue(structured));

        Assert.Contains(dto.Content, item => item.Text.Contains("\"query\": \"hello world\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_Should_Return_Failure_When_Search_Service_Fails()
    {
        var web = Substitute.For<IWebSearchService>();
        var logger = Substitute.For<ILogger<WebSearchToolHandler>>();

        web.SearchAsync(Arg.Any<McpServer.Application.Web.Commands.SearchWebCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<IReadOnlyList<WebSearchResult>>>(
                LanguageExt.Common.Error.New("web search failed")));

        var handler = new WebSearchToolHandler(web, logger);

        var result = await handler.Handle(new WebSearchRequest("hello world", 2), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected failure."),
            Fail: value => value);

        Assert.Equal("web search failed", error.Message);
    }
}
