using LanguageExt;
using McpServer.AgentRouter.Domain.AgentRuns;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Coordinates agent run lifecycle operations.
/// </summary>
public interface IAgentRunService
{
    /// <summary>
    /// Starts a new agent run from the supplied request.
    /// </summary>
    ValueTask<Fin<AgentRun>> StartRunAsync(
        AgentRunRequest? request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a previously created agent run.
    /// </summary>
    ValueTask<Fin<AgentRun>> GetRunAsync(
        string runId,
        CancellationToken cancellationToken);
}
