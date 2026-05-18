using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ISshExecutionTraceWriter
{
    ValueTask<Fin<Unit>> WriteAsync(
        SshExecutionTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
