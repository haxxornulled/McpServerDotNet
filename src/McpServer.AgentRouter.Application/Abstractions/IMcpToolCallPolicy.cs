using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IMcpToolCallPolicy
{
    ValueTask<Fin<McpToolCallPolicyDecision>> EvaluateAsync(
        McpToolCallCommand command,
        CancellationToken cancellationToken);
}
