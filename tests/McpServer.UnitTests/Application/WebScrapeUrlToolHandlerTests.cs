using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WebScrapeUrlToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_Structured_Scrape_Result()
    {
        var web = Substitute.For<IWebScrapeService>();
        var logger = Substitute.For<ILogger<WebScrapeUrlToolHandler>>();

        web.ScrapeAsync(Arg.Any<McpServer.Application.Web.Commands.ScrapeWebCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<WebScrapeResult>>(new WebScrapeResult(
                "http://127.0.0.1/page",
                "Loopback Scrape",
                "article.card",
                2,
                false,
                12,
                [
                    new WebScrapeMatch(1, "article", "First Article", "http://127.0.0.1/articles/one", "data-id", "one"),
                    new WebScrapeMatch(2, "article", "Second Article", "http://127.0.0.1/articles/two", "data-id", "two")
                ])));

        var handler = new WebScrapeUrlToolHandler(web, logger);
        var result = await handler.Handle(new WebScrapeUrlRequest("http://127.0.0.1/page", "article.card", "data-id", 2, 30), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = dto.StructuredContent ?? throw new InvalidOperationException("Expected structured content.");
        var queryProperty = structured.GetType().GetProperty("Selector");
        Assert.NotNull(queryProperty);
        Assert.Equal("article.card", queryProperty!.GetValue(structured));

        var resultCountProperty = structured.GetType().GetProperty("MatchCount");
        Assert.NotNull(resultCountProperty);
        Assert.Equal(2, resultCountProperty!.GetValue(structured));

        Assert.Contains(dto.Content, item => item.Text.Contains("\"selector\": \"article.card\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_Should_Return_Failure_When_Scrape_Service_Fails()
    {
        var web = Substitute.For<IWebScrapeService>();
        var logger = Substitute.For<ILogger<WebScrapeUrlToolHandler>>();

        web.ScrapeAsync(Arg.Any<McpServer.Application.Web.Commands.ScrapeWebCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<WebScrapeResult>>(LanguageExt.Common.Error.New("scrape failed")));

        var handler = new WebScrapeUrlToolHandler(web, logger);

        var result = await handler.Handle(new WebScrapeUrlRequest("http://127.0.0.1/page", "article.card"), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected failure."),
            Fail: value => value);

        Assert.Equal("scrape failed", error.Message);
    }
}
