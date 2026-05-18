using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ISshCommandExecutor
{
    ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(
        SshExecutionCommand command,
        CancellationToken cancellationToken);
}
