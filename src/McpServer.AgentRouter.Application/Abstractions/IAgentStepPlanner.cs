using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentStepPlanner
{
    ValueTask<Fin<AgentPlannedStep>> PlanNextStepAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken);
}
