using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WebFetchToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_Text_Content()
    {
        var web = Substitute.For<IWebFetchService>();
        var logger = Substitute.For<ILogger<WebFetchUrlToolHandler>>();

        web.FetchUrlAsync(Arg.Any<McpServer.Application.Web.Commands.FetchUrlCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<FetchedPageResult>>(new FetchedPageResult(
                Url: "https://example.com",
                Title: "Example",
                Content: "Hello world",
                ContentType: "text/html",
                StatusCode: 200,
                FetchTimeMs: 10)));

        var handler = new WebFetchUrlToolHandler(web, logger);
        var result = await handler.Handle(new WebFetchUrlRequest("https://example.com"), CancellationToken.None);

        Assert.True(result.IsSucc);
    }
}
