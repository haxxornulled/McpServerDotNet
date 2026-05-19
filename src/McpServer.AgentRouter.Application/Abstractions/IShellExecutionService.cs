using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Coordinates shell execution requests.
/// </summary>
public interface IShellExecutionService
{
    /// <summary>
    /// Executes a shell request through policy and transport layers.
    /// </summary>
    ValueTask<Fin<ShellExecutionResponse>> ExecuteAsync(
        ShellExecutionRequest? request,
        CancellationToken cancellationToken);
}
