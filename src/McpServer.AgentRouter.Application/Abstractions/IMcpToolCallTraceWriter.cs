using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Writes trace records for MCP tool calls.
/// </summary>
public interface IMcpToolCallTraceWriter
{
    /// <summary>
    /// Persists a trace record for a tool call.
    /// </summary>
    ValueTask<Fin<Unit>> WriteAsync(
        McpToolCallTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
