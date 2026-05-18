using McpServer.AgentRouter.Application.Runtime;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ILocalModelRuntimeManager
{
    ValueTask EnsureAvailableAsync(
        LocalModelRuntimeStartupSettings settings,
        CancellationToken cancellationToken);

    ValueTask StopManagedRuntimeAsync(CancellationToken cancellationToken);
}
