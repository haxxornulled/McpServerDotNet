using LanguageExt;
using McpServer.AgentRouter.Domain.AgentRuns;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentRunService
{
    ValueTask<Fin<AgentRun>> StartRunAsync(
        AgentRunRequest? request,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRun>> GetRunAsync(
        string runId,
        CancellationToken cancellationToken);
}
