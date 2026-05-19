using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Writes trace records for shell executions.
/// </summary>
public interface IShellExecutionTraceWriter
{
    /// <summary>
    /// Persists a trace record for a shell execution.
    /// </summary>
    ValueTask<Fin<Unit>> WriteAsync(
        ShellExecutionTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
