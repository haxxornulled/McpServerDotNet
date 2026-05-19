using System.Net;
using System.Text;
using McpServer.Application.Web.Commands;
using McpServer.Infrastructure.Web;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class DuckDuckGoHtmlSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_Should_Request_Search_Endpoint_And_Return_Normalized_Results()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <html>
                  <head><title>Loopback Search</title></head>
                  <body>
                    <div class="result results_links results_links_deep web-result">
                      <a rel="nofollow" class="result__a" href="/results/one"><span>Result One</span> for MCP server protocol</a>
                      <a class="result__snippet">Snippet one for MCP server protocol</a>
                    </div>
                    <div class="result results_links results_links_deep web-result">
                      <a rel="nofollow" class="result__a" href="/results/two">Result Two for MCP server protocol</a>
                      <div class="result__snippet">Snippet two for MCP server protocol</div>
                    </div>
                    <div class="result results_links results_links_deep web-result">
                      <a rel="nofollow" class="result__a" href="/results/three">Result Three for MCP server protocol</a>
                      <div class="result__snippet">Snippet three for MCP server protocol</div>
                    </div>
                  </body>
                </html>
                """, Encoding.UTF8, "text/html")
        });
        var factory = new SingleClientHttpClientFactory(handler);
        var policy = new WebPolicy(
            new HashSet<string>(["127.0.0.1"], StringComparer.OrdinalIgnoreCase),
            allowLocalLoopbackHosts: true,
            searchBaseUrl: "http://127.0.0.1/search?q=");
        var logger = Substitute.For<ILogger<DuckDuckGoHtmlSearchProvider>>();
        var sut = new DuckDuckGoHtmlSearchProvider(factory, policy, logger);

        var result = await sut.SearchAsync(new SearchWebCommand("MCP server protocol", 2), CancellationToken.None);

        Assert.True(result.IsSucc);
        var items = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal(2, items.Count);

        Assert.Collection(items,
            item =>
            {
                Assert.Equal("Result One for MCP server protocol", item.Title);
                Assert.Equal("http://127.0.0.1/results/one", item.Url);
                Assert.Contains("Snippet one for MCP server protocol", item.Snippet, StringComparison.Ordinal);
                Assert.Equal(1.0, item.Relevance);
            },
            item =>
            {
                Assert.Equal("Result Two for MCP server protocol", item.Title);
                Assert.Equal("http://127.0.0.1/results/two", item.Url);
                Assert.Contains("Snippet two for MCP server protocol", item.Snippet, StringComparison.Ordinal);
                Assert.Equal(0.9, item.Relevance);
            });

        Assert.NotNull(handler.RequestUri);
        Assert.Equal("/search", handler.RequestUri!.AbsolutePath);
        Assert.Equal("q=MCP%20server%20protocol", handler.RequestUri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task SearchAsync_Should_Normalize_DuckDuckGo_Redirect_Urls()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                <html>
                  <body>
                    <div class="result results_links results_links_deep web-result">
                      <a rel="nofollow" class="result__a" href="https://duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Farticle&rut=abc123">Result One</a>
                      <div class="result__snippet">Snippet one</div>
                    </div>
                  </body>
                </html>
                """, Encoding.UTF8, "text/html")
        });
        var factory = new SingleClientHttpClientFactory(handler);
        var policy = new WebPolicy(
            new HashSet<string>(["127.0.0.1"], StringComparer.OrdinalIgnoreCase),
            allowLocalLoopbackHosts: true,
            searchBaseUrl: "http://127.0.0.1/search?q=");
        var logger = Substitute.For<ILogger<DuckDuckGoHtmlSearchProvider>>();
        var sut = new DuckDuckGoHtmlSearchProvider(factory, policy, logger);

        var result = await sut.SearchAsync(new SearchWebCommand("MCP server protocol", 1), CancellationToken.None);

        Assert.True(result.IsSucc);
        var items = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var item = Assert.Single(items);
        Assert.Equal("https://example.com/article", item.Url);
        Assert.Equal("Result One", item.Title);
        Assert.Contains("Snippet one", item.Snippet, StringComparison.Ordinal);
    }

    private sealed class SingleClientHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response = response;

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            if (request.Content is not null)
            {
                _ = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _response;
        }
    }
}
