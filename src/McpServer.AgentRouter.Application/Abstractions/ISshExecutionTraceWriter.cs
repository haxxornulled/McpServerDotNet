using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Writes trace records for SSH executions.
/// </summary>
public interface ISshExecutionTraceWriter
{
    /// <summary>
    /// Persists a trace record for an SSH execution.
    /// </summary>
    ValueTask<Fin<Unit>> WriteAsync(
        SshExecutionTraceRecord traceRecord,
        CancellationToken cancellationToken);
}
