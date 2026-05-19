using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Executes a planned agent loop step.
/// </summary>
public interface IAgentToolExecutor
{
    /// <summary>
    /// Executes the requested tool step and returns the execution result.
    /// </summary>
    ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken);
}
