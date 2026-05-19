using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Executes SSH commands through the transport layer.
/// </summary>
public interface ISshCommandExecutor
{
    /// <summary>
    /// Executes the supplied SSH command.
    /// </summary>
    ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(
        SshExecutionCommand command,
        CancellationToken cancellationToken);
}
