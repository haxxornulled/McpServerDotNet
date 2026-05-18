using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentLoopValidator
{
    ValueTask<Fin<AgentLoopValidation>> ValidateAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken);
}
