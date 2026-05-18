using LanguageExt;
using McpServer.AgentRouter.Domain.AgentRuns;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentRunStore
{
    ValueTask SaveAsync(
        AgentRun run,
        AgentRunRequest? request,
        CancellationToken cancellationToken);

    ValueTask<Fin<AgentRun>> GetAsync(
        string runId,
        CancellationToken cancellationToken);
}
