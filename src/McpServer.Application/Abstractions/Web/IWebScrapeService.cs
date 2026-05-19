using LanguageExt;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Abstractions.Web;

public interface IWebScrapeService
{
    ValueTask<Fin<WebScrapeResult>> ScrapeAsync(ScrapeWebCommand command, CancellationToken ct);
}
