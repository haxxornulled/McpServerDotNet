using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IMcpToolCallClient
{
    ValueTask<Fin<McpToolInvocationResult>> CallToolAsync(
        McpToolCallCommand command,
        CancellationToken cancellationToken);
}
