using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentTraceWriter
{
    ValueTask<Fin<Unit>> WriteStepAsync(
        AgentLoopRun run,
        AgentLoopStep step,
        CancellationToken cancellationToken);

    ValueTask<Fin<Unit>> WriteRunAsync(
        AgentLoopRun run,
        CancellationToken cancellationToken);
}
