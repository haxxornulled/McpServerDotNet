using LanguageExt;
using McpServer.AgentRouter.Application.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IMcpToolCatalogClient
{
    ValueTask<Fin<McpToolCatalogSnapshot>> ListToolsAsync(CancellationToken cancellationToken);
}
