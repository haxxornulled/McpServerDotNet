using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Coordinates MCP tool call handling.
/// </summary>
public interface IMcpToolCallService
{
    /// <summary>
    /// Executes a tool call request through the MCP pipeline.
    /// </summary>
    ValueTask<Fin<McpToolCallResponse>> CallToolAsync(
        McpToolCallRequest? request,
        CancellationToken cancellationToken);
}
