using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Produces the next planned step for an agent loop.
/// </summary>
public interface IAgentStepPlanner
{
    /// <summary>
    /// Plans the next step for the supplied loop context.
    /// </summary>
    ValueTask<Fin<AgentPlannedStep>> PlanNextStepAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken);
}
