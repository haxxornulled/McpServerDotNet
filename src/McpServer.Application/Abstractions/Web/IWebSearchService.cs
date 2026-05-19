using LanguageExt;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Abstractions.Web;

public interface IWebSearchService
{
    ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchAsync(SearchWebCommand command, CancellationToken ct);
}
