using LanguageExt;
using McpServer.AgentRouter.Application.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Retrieves the active MCP tool catalog from the transport layer.
/// </summary>
public interface IMcpToolCatalogClient
{
    /// <summary>
    /// Lists the available MCP tools.
    /// </summary>
    ValueTask<Fin<McpToolCatalogSnapshot>> ListToolsAsync(CancellationToken cancellationToken);
}
