using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IShellExecutionService
{
    ValueTask<Fin<ShellExecutionResponse>> ExecuteAsync(
        ShellExecutionRequest? request,
        CancellationToken cancellationToken);
}
