using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentResultInspector
{
    ValueTask<Fin<AgentResultInspection>> InspectAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        AgentToolExecutionResult executionResult,
        CancellationToken cancellationToken);
}
