using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Evaluates whether a planned loop tool step may execute.
/// </summary>
public interface IToolExecutionPolicy
{
    /// <summary>
    /// Evaluates the planned step in the supplied loop context.
    /// </summary>
    ValueTask<Fin<ToolExecutionPolicyDecision>> EvaluateAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        CancellationToken cancellationToken);
}
