using LanguageExt;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Abstractions.Web;

public interface IWebFetchService
{
    ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct);
}
