using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.UnitTests;

internal sealed class FakeAgentStepPlanner : IAgentStepPlanner
{
    public ValueTask<Fin<AgentPlannedStep>> PlanNextStepAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sequence = context.CompletedStepCount + 1;
        var phase = sequence == 1 ? AgentStepPhase.Observe : AgentStepPhase.Validate;
        var capability = sequence == 1 ? "fake.observe" : "fake.validate";

        return new ValueTask<Fin<AgentPlannedStep>>(Fin<AgentPlannedStep>.Succ(new AgentPlannedStep
        {
            Phase = phase,
            Capability = capability,
            ToolName = capability,
            RiskLevel = ToolRiskLevel.Low,
            InputSummary = $"Fake loop step {sequence} for goal: {context.Run.Goal}"
        }));
    }
}

internal sealed class BasicAgentResultInspector : IAgentResultInspector
{
    public ValueTask<Fin<AgentResultInspection>> InspectAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        AgentToolExecutionResult executionResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(executionResult);

        if (executionResult.Status == ToolExecutionStatus.Failed)
        {
            return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
            {
                Decision = AgentDecisionType.StopFailed,
                OutputSummary = executionResult.ErrorMessage ?? executionResult.OutputSummary,
                FinalResult = executionResult.ErrorMessage ?? executionResult.OutputSummary
            }));
        }

        var isLastStep = context.CompletedStepCount >= context.MaxSteps;

        return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
        {
            Decision = isLastStep ? AgentDecisionType.StopSucceeded : AgentDecisionType.Continue,
            OutputSummary = executionResult.OutputSummary,
            FinalResult = isLastStep ? executionResult.OutputSummary : null
        }));
    }
}
