using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Executes low-level MCP tool calls over the configured transport.
/// </summary>
public interface IMcpToolCallClient
{
    /// <summary>
    /// Executes the supplied MCP tool call command.
    /// </summary>
    ValueTask<Fin<McpToolInvocationResult>> CallToolAsync(
        McpToolCallCommand command,
        CancellationToken cancellationToken);
}
