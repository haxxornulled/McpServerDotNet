using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using LanguageExt;

namespace McpServer.Application.Abstractions.Web
{
    public interface IWebAccessService
    {
        ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchWebAsync(SearchWebCommand command, CancellationToken ct);
        ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct);
    }
}