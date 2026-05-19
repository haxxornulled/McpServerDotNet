using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Coordinates SSH execution requests.
/// </summary>
public interface ISshExecutionService
{
    /// <summary>
    /// Executes an SSH request through policy and transport layers.
    /// </summary>
    ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(
        SshExecutionRequest? request,
        CancellationToken cancellationToken);
}
