using LanguageExt;
using McpServer.AgentRouter.Domain.AgentRuns;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Persists and retrieves agent run state.
/// </summary>
public interface IAgentRunStore
{
    /// <summary>
    /// Saves a run and its original request.
    /// </summary>
    ValueTask SaveAsync(
        AgentRun run,
        AgentRunRequest? request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads a run by identifier.
    /// </summary>
    ValueTask<Fin<AgentRun>> GetAsync(
        string runId,
        CancellationToken cancellationToken);
}
