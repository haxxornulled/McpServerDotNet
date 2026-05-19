using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Runs the autonomous agent loop pipeline.
/// </summary>
public interface IAutonomousLoopRunner
{
    /// <summary>
    /// Runs an autonomous loop for the supplied request.
    /// </summary>
    ValueTask<Fin<AgentLoopRun>> RunAsync(
        AgentLoopRequest? request,
        CancellationToken cancellationToken);
}
