using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Inspects tool execution output and translates it into loop guidance.
/// </summary>
public interface IAgentResultInspector
{
    /// <summary>
    /// Inspects a completed tool execution and returns the follow-up decision.
    /// </summary>
    ValueTask<Fin<AgentResultInspection>> InspectAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        AgentToolExecutionResult executionResult,
        CancellationToken cancellationToken);
}
