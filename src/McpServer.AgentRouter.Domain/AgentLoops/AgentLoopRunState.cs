namespace McpServer.AgentRouter.Domain.AgentLoops;

/// <summary>
/// Owns the mutable state transitions for an agent loop run.
/// </summary>
public sealed class AgentLoopRunState
{
    /// <summary>
    /// Initializes a new run state wrapper.
    /// </summary>
    private AgentLoopRunState(AgentLoopRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>
    /// Gets the backing run model.
    /// </summary>
    public AgentLoopRun Run { get; }

    /// <summary>
    /// Starts a new loop run for the supplied goal.
    /// </summary>
    public static AgentLoopRunState Start(string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        var now = DateTimeOffset.UtcNow;
        var run = new AgentLoopRun
        {
            Id = "loop-" + Guid.NewGuid().ToString("N"),
            Status = AgentLoopStatusNames.Running,
            Goal = goal.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        return new AgentLoopRunState(run);
    }

    /// <summary>
    /// Creates a new step entry and appends it to the run.
    /// </summary>
    public AgentLoopStep BeginStep(int sequence, AgentPlannedStep plannedStep)
    {
        ArgumentNullException.ThrowIfNull(plannedStep);

        var step = new AgentLoopStep
        {
            StepId = "step-" + Guid.NewGuid().ToString("N"),
            Sequence = sequence,
            Phase = plannedStep.Phase,
            Capability = plannedStep.Capability,
            ToolName = plannedStep.ToolName,
            RiskLevel = plannedStep.RiskLevel,
            Status = ToolExecutionStatus.Pending,
            Decision = AgentDecisionType.Continue,
            StartedAt = DateTimeOffset.UtcNow,
            InputSummary = plannedStep.InputSummary
        };

        Run.Steps.Add(step);
        return step;
    }

    /// <summary>
    /// Applies a policy decision to an in-flight step.
    /// </summary>
    public void ApplyPolicyDecision(
        AgentLoopStep step,
        ToolExecutionPolicyDecision decision)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(decision);

        step.PolicyDecision = decision.Decision;
    }

    /// <summary>
    /// Completes a step as denied by policy.
    /// </summary>
    public void CompleteDeniedStep(
        AgentLoopStep step,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        step.Status = ToolExecutionStatus.Denied;
        step.Decision = AgentDecisionType.StopFailed;
        step.OutputSummary = reason;
        step.ErrorMessage = reason;
        step.CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Completes a step as failed.
    /// </summary>
    public void CompleteFailedStep(
        AgentLoopStep step,
        string errorMessage,
        long elapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        step.Status = ToolExecutionStatus.Failed;
        step.Decision = AgentDecisionType.StopFailed;
        step.OutputSummary = errorMessage;
        step.ErrorMessage = errorMessage;
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>
    /// Completes a step with the outcome returned from a tool execution.
    /// </summary>
    public void CompleteExecutedStep(
        AgentLoopStep step,
        AgentToolExecutionResult toolResult,
        long fallbackElapsedMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(toolResult);

        step.Status = toolResult.Status;
        step.OutputSummary = toolResult.OutputSummary;
        step.ErrorMessage = toolResult.ErrorMessage;
        step.TraceId = toolResult.TraceId;
        step.CompletedAt = DateTimeOffset.UtcNow;
        step.ElapsedMilliseconds = toolResult.ElapsedMilliseconds > 0
            ? toolResult.ElapsedMilliseconds
            : fallbackElapsedMilliseconds;
    }

    /// <summary>
    /// Applies inspection feedback to the current step.
    /// </summary>
    public void ApplyInspection(
        AgentLoopStep step,
        AgentResultInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(inspection);

        step.Decision = inspection.Decision;
        if (!string.IsNullOrWhiteSpace(inspection.OutputSummary))
        {
            step.OutputSummary = inspection.OutputSummary;
        }
    }

    /// <summary>
    /// Updates the run's last-modified timestamp.
    /// </summary>
    public void Touch()
    {
        Run.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Marks the run as completed with a final result.
    /// </summary>
    public void MarkCompleted(string result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);

        var now = DateTimeOffset.UtcNow;
        Run.Status = AgentLoopStatusNames.Completed;
        Run.Result = result;
        Run.Error = null;
        Run.UpdatedAt = now;
        Run.CompletedAt = now;
    }

    /// <summary>
    /// Marks the run as failed with an error payload.
    /// </summary>
    public void MarkFailed(
        string errorMessage,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        var now = DateTimeOffset.UtcNow;
        Run.Status = AgentLoopStatusNames.Failed;
        Run.Result = null;
        Run.Error = new AgentLoopError
        {
            Message = errorMessage,
            Type = "agent_loop_error",
            Code = errorCode
        };
        Run.UpdatedAt = now;
        Run.CompletedAt = now;
    }
}
