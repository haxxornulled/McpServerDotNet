using System.Text.Json;

namespace McpServer.AgentRouter.Domain.AgentLoops;

/// <summary>
/// Describes the inputs required to start or resume an agent loop run.
/// </summary>
public sealed class AgentLoopRequest
{
    /// <summary>
    /// Gets or sets the user goal that the loop should work toward.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of loop steps to execute.
    /// </summary>
    public int? MaxSteps { get; set; }

    /// <summary>
    /// Gets or sets the capability names that the loop may use.
    /// </summary>
    public IList<string> AllowedCapabilities { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the tool name when the request targets a single tool-driven path.
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Gets or sets the raw tool arguments supplied by the caller.
    /// </summary>
    public JsonElement? Arguments { get; set; }
}

/// <summary>
/// Represents the persisted state of an agent loop run.
/// </summary>
public sealed class AgentLoopRun
{
    /// <summary>
    /// Gets or sets the unique identifier for the run.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current run status.
    /// </summary>
    public string Status { get; set; } = AgentLoopStatusNames.Queued;

    /// <summary>
    /// Gets or sets the loop goal.
    /// </summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time the run was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last time the run was updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the time the run completed, if applicable.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the final result text.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the terminal error details, if the run failed.
    /// </summary>
    public AgentLoopError? Error { get; set; }

    /// <summary>
    /// Gets or sets the ordered list of steps executed by the run.
    /// </summary>
    public IList<AgentLoopStep> Steps { get; set; } = new List<AgentLoopStep>();
}

/// <summary>
/// Describes a single step within an agent loop run.
/// </summary>
public sealed class AgentLoopStep
{
    /// <summary>
    /// Gets or sets the unique identifier for the step.
    /// </summary>
    public string StepId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the step sequence number.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Gets or sets the loop phase that produced this step.
    /// </summary>
    public AgentStepPhase Phase { get; set; } = AgentStepPhase.Observe;

    /// <summary>
    /// Gets or sets the capability associated with the step.
    /// </summary>
    public string Capability { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool name used for the step.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assessed risk level for the step tool.
    /// </summary>
    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.Low;

    /// <summary>
    /// Gets or sets the final status of the step execution.
    /// </summary>
    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Pending;

    /// <summary>
    /// Gets or sets the loop decision taken after evaluating the step.
    /// </summary>
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    /// <summary>
    /// Gets or sets the policy decision summary for the step.
    /// </summary>
    public string PolicyDecision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time the step started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the time the step completed, if it has completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets a short summary of the step input.
    /// </summary>
    public string InputSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a short summary of the step output.
    /// </summary>
    public string OutputSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the failure message when the step did not succeed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the trace identifier associated with the step.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// Describes a terminal error for an agent loop run.
/// </summary>
public sealed class AgentLoopError
{
    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable error type name.
    /// </summary>
    public string Type { get; set; } = "agent_loop_error";

    /// <summary>
    /// Gets or sets the stable error code.
    /// </summary>
    public string Code { get; set; } = "agent_loop_failed";
}

/// <summary>
/// Identifies the phase of an agent loop step.
/// </summary>
public enum AgentStepPhase
{
    /// <summary>Observation phase.</summary>
    Observe,
    /// <summary>Hypothesis phase.</summary>
    Hypothesize,
    /// <summary>Planning phase.</summary>
    Plan,
    /// <summary>Execution phase.</summary>
    Act,
    /// <summary>Validation phase.</summary>
    Validate,
    /// <summary>Completion phase.</summary>
    Complete,
    /// <summary>Failure phase.</summary>
    Failed
}

/// <summary>
/// Identifies the action taken by the loop after evaluating a step.
/// </summary>
public enum AgentDecisionType
{
    /// <summary>Continue the loop.</summary>
    Continue,
    /// <summary>Retry the step or run.</summary>
    Retry,
    /// <summary>Attempt a repair path.</summary>
    Repair,
    /// <summary>Escalate the issue.</summary>
    Escalate,
    /// <summary>Stop successfully.</summary>
    StopSucceeded,
    /// <summary>Stop with failure.</summary>
    StopFailed
}

/// <summary>
/// Describes the risk level assigned to a tool action.
/// </summary>
public enum ToolRiskLevel
{
    /// <summary>Low risk.</summary>
    Low,
    /// <summary>Medium risk.</summary>
    Medium,
    /// <summary>High risk.</summary>
    High,
    /// <summary>Critical risk.</summary>
    Critical
}

/// <summary>
/// Describes the execution state of a tool operation.
/// </summary>
public enum ToolExecutionStatus
{
    /// <summary>Pending execution.</summary>
    Pending,
    /// <summary>Skipped execution.</summary>
    Skipped,
    /// <summary>Currently running.</summary>
    Running,
    /// <summary>Execution succeeded.</summary>
    Succeeded,
    /// <summary>Execution failed.</summary>
    Failed,
    /// <summary>Execution was denied by policy.</summary>
    Denied
}

/// <summary>
/// Provides stable string values for agent loop run states.
/// </summary>
public static class AgentLoopStatusNames
{
    /// <summary>Queued run state.</summary>
    public const string Queued = "queued";
    /// <summary>Running run state.</summary>
    public const string Running = "running";
    /// <summary>Completed run state.</summary>
    public const string Completed = "completed";
    /// <summary>Failed run state.</summary>
    public const string Failed = "failed";
    /// <summary>Cancelled run state.</summary>
    public const string Cancelled = "cancelled";
}
