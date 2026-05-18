using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IShellExecutionTraceWriter
{
    ValueTask<Fin<Unit>> WriteAsync(
        ShellExecutionTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
