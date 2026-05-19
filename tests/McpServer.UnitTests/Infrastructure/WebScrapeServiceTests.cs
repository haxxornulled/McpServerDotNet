using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using McpServer.Infrastructure.Web;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class WebScrapeServiceTests
{
    [Fact]
    public async Task ScrapeAsync_Should_Extract_Matching_Elements_And_Normalize_Links()
    {
        var fetch = Substitute.For<IWebFetchService>();
        fetch.FetchUrlAsync(Arg.Any<FetchUrlCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<LanguageExt.Fin<FetchedPageResult>>(new FetchedPageResult(
                Url: "http://127.0.0.1/page",
                Title: "Loopback Scrape",
                Content: """
                    <html>
                      <head><title>Loopback Scrape</title></head>
                      <body>
                        <article class="card" data-id="one">
                          <h2>First Article</h2>
                          <a href="/articles/one">Read more</a>
                        </article>
                        <article class="card" data-id="two">
                          <h2>Second Article</h2>
                          <a href="/articles/two">Read more</a>
                        </article>
                      </body>
                    </html>
                    """,
                ContentType: "text/html; charset=utf-8",
                StatusCode: 200,
                FetchTimeMs: 12)));

        var sut = new WebScrapeService(fetch, Substitute.For<ILogger<WebScrapeService>>());

        var result = await sut.ScrapeAsync(new ScrapeWebCommand("http://127.0.0.1/page", "article.card", "data-id", 2), CancellationToken.None);

        Assert.True(result.IsSucc);
        var scrape = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal("http://127.0.0.1/page", scrape.Url);
        Assert.Equal("Loopback Scrape", scrape.Title);
        Assert.Equal("article.card", scrape.Selector);
        Assert.Equal(2, scrape.MatchCount);
        Assert.False(scrape.Truncated);
        Assert.Equal(2, scrape.Matches.Count);

        Assert.Collection(scrape.Matches,
            match =>
            {
                Assert.Equal(1, match.Rank);
                Assert.Equal("article", match.TagName);
                Assert.Equal("First Article Read more", match.Text);
                Assert.Equal("http://127.0.0.1/articles/one", match.Href);
                Assert.Equal("data-id", match.AttributeName);
                Assert.Equal("one", match.AttributeValue);
            },
            match =>
            {
                Assert.Equal(2, match.Rank);
                Assert.Equal("article", match.TagName);
                Assert.Equal("Second Article Read more", match.Text);
                Assert.Equal("http://127.0.0.1/articles/two", match.Href);
                Assert.Equal("data-id", match.AttributeName);
                Assert.Equal("two", match.AttributeValue);
            });
    }
}
