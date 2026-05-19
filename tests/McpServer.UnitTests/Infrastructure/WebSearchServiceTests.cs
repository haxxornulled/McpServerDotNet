using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Infrastructure.Web;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class WebSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_Should_Propagate_Provider_Exception()
    {
        var provider = new ThrowingWebSearchProvider();
        var sut = new WebSearchService(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SearchAsync(new SearchWebCommand("hello world", 5), CancellationToken.None).AsTask());
    }

    private sealed class ThrowingWebSearchProvider : IWebSearchProvider
    {
        public ValueTask<LanguageExt.Fin<IReadOnlyList<McpServer.Application.Web.Results.WebSearchResult>>> SearchAsync(
            SearchWebCommand command,
            CancellationToken ct)
        {
            throw new InvalidOperationException("provider exploded");
        }
    }
}
