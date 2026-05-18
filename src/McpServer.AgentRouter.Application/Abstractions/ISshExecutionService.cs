using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ISshExecutionService
{
    ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(
        SshExecutionRequest? request,
        CancellationToken cancellationToken);
}
