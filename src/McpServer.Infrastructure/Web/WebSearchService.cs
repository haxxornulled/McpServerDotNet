using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Infrastructure.Web;

public sealed class WebSearchService(
    IWebSearchProvider webSearchProvider) : IWebSearchService
{
    public async ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchAsync(SearchWebCommand command, CancellationToken ct)
    {
        return await webSearchProvider.SearchAsync(command, ct).ConfigureAwait(false);
    }
}
