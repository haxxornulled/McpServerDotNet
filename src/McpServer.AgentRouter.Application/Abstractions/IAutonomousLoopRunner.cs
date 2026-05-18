using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAutonomousLoopRunner
{
    ValueTask<Fin<AgentLoopRun>> RunAsync(
        AgentLoopRequest? request,
        CancellationToken cancellationToken);
}
