using LanguageExt;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Abstractions
{
    public interface IWebService
    {
        ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchAsync(SearchWebCommand command, CancellationToken ct);
        ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct);
    }
}