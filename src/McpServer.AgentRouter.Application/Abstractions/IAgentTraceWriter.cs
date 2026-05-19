using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Writes agent loop trace artifacts.
/// </summary>
public interface IAgentTraceWriter
{
    /// <summary>
    /// Writes a trace entry for a single loop step.
    /// </summary>
    ValueTask<Fin<Unit>> WriteStepAsync(
        AgentLoopRun run,
        AgentLoopStep step,
        CancellationToken cancellationToken);

    /// <summary>
    /// Writes a trace entry for the completed run.
    /// </summary>
    ValueTask<Fin<Unit>> WriteRunAsync(
        AgentLoopRun run,
        CancellationToken cancellationToken);
}
