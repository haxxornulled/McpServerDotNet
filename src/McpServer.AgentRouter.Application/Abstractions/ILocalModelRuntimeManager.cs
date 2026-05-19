using McpServer.AgentRouter.Application.Runtime;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Controls the managed local model runtime lifecycle.
/// </summary>
public interface ILocalModelRuntimeManager
{
    /// <summary>
    /// Ensures the managed runtime is available for use.
    /// </summary>
    ValueTask EnsureAvailableAsync(
        LocalModelRuntimeStartupSettings settings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stops the managed runtime if it was started by the application.
    /// </summary>
    ValueTask StopManagedRuntimeAsync(CancellationToken cancellationToken);
}
