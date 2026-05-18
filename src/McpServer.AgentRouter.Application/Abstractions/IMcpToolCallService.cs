using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IMcpToolCallService
{
    ValueTask<Fin<McpToolCallResponse>> CallToolAsync(
        McpToolCallRequest? request,
        CancellationToken cancellationToken);
}
