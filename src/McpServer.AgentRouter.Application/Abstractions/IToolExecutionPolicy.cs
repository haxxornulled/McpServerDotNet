using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IToolExecutionPolicy
{
    ValueTask<Fin<ToolExecutionPolicyDecision>> EvaluateAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        CancellationToken cancellationToken);
}
