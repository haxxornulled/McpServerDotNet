using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.AgentLoops;

/// <summary>
/// Stops the loop once it has reached its configured step bound.
/// </summary>
public sealed class BoundedAgentLoopValidator : IAgentLoopValidator
{
    /// <summary>
    /// Validates the loop against its step limit.
    /// </summary>
    public ValueTask<Fin<AgentLoopValidation>> ValidateAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Run.Steps.Count >= context.MaxSteps)
        {
            return new ValueTask<Fin<AgentLoopValidation>>(Fin<AgentLoopValidation>.Succ(new AgentLoopValidation
            {
                Decision = AgentDecisionType.StopSucceeded,
                Message = "Autonomous loop reached its bounded validation stop."
            }));
        }

        return new ValueTask<Fin<AgentLoopValidation>>(Fin<AgentLoopValidation>.Succ(new AgentLoopValidation
        {
            Decision = AgentDecisionType.Continue
        }));
    }
}
