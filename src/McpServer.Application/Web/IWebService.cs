using LanguageExt;
using McpServer.Application.Abstractions;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Web
{
    public interface IWebService
    {
        ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchAsync(SearchWebCommand command, CancellationToken ct);
        ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct);
    }
}