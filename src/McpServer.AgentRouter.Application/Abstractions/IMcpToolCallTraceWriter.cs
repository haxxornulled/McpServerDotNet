using LanguageExt;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IMcpToolCallTraceWriter
{
    ValueTask<Fin<Unit>> WriteAsync(
        McpToolCallTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
