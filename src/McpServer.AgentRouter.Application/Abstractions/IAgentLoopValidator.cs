using LanguageExt;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Validates agent loop state before the next step is executed.
/// </summary>
public interface IAgentLoopValidator
{
    /// <summary>
    /// Evaluates the current loop context and returns a validation decision.
    /// </summary>
    ValueTask<Fin<AgentLoopValidation>> ValidateAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken);
}
