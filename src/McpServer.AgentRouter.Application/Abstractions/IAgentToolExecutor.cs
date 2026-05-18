using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentToolExecutor
{
    ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken);
}
