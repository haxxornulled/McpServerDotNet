using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Evaluates whether an MCP tool call should be allowed.
/// </summary>
public interface IMcpToolCallPolicy
{
    /// <summary>
    /// Evaluates the supplied tool call command.
    /// </summary>
    ValueTask<Fin<McpToolCallPolicyDecision>> EvaluateAsync(
        McpToolCallCommand command,
        CancellationToken cancellationToken);
}
