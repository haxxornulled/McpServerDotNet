using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Executes shell commands against the local process environment.
/// </summary>
public interface IShellCommandExecutor
{
    /// <summary>
    /// Executes the supplied shell command.
    /// </summary>
    ValueTask<Fin<ShellCommandExecutionResult>> ExecuteAsync(
        ShellExecutionCommand command,
        CancellationToken cancellationToken);
}
