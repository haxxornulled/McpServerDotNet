using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.AgentLoops;

public sealed class BoundedAgentLoopValidator : IAgentLoopValidator
{
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
