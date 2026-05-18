using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IShellCommandExecutor
{
    ValueTask<Fin<ShellCommandExecutionResult>> ExecuteAsync(
        ShellExecutionCommand command,
        CancellationToken cancellationToken);
}
