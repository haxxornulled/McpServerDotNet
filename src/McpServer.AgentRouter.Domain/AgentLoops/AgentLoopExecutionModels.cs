using System.Text.Json;

namespace McpServer.AgentRouter.Domain.AgentLoops;

/// <summary>
/// Holds the live execution context for an agent loop run.
/// </summary>
public sealed class AgentLoopExecutionContext
{
    private readonly string[] _allowedCapabilities;

    /// <summary>
    /// Initializes a new execution context for an agent loop run.
    /// </summary>
    public AgentLoopExecutionContext(
        AgentLoopRun run,
        AgentLoopRequest request,
        int maxSteps,
        IReadOnlyList<string> allowedCapabilities,
        int maxToolCalls = int.MaxValue,
        int maxRuntimeSeconds = int.MaxValue)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        MaxSteps = maxSteps;
        MaxToolCalls = maxToolCalls;
        MaxRuntimeSeconds = maxRuntimeSeconds;
        _allowedCapabilities = allowedCapabilities?.ToArray()
            ?? throw new ArgumentNullException(nameof(allowedCapabilities));
    }

    /// <summary>
    /// Gets the backing run being executed.
    /// </summary>
    public AgentLoopRun Run { get; }

    /// <summary>
    /// Gets the original request that started the run.
    /// </summary>
    public AgentLoopRequest Request { get; }

    /// <summary>
    /// Gets the maximum number of steps allowed for the run.
    /// </summary>
    public int MaxSteps { get; }

    /// <summary>
    /// Gets the maximum number of tool calls allowed for the run.
    /// </summary>
    public int MaxToolCalls { get; }

    /// <summary>
    /// Gets the maximum runtime allowed for the run in seconds.
    /// </summary>
    public int MaxRuntimeSeconds { get; }

    /// <summary>
    /// Gets the capabilities the run may use.
    /// </summary>
    public IReadOnlyList<string> AllowedCapabilities => _allowedCapabilities;

    /// <summary>
    /// Gets the number of completed steps currently recorded on the run.
    /// </summary>
    public int CompletedStepCount => Run.Steps.Count;
}

/// <summary>
/// Describes a candidate step planned by the loop.
/// </summary>
public sealed class AgentPlannedStep
{
    /// <summary>
    /// Gets or sets the loop phase for the step.
    /// </summary>
    public AgentStepPhase Phase { get; set; } = AgentStepPhase.Observe;

    /// <summary>
    /// Gets or sets the capability the step uses.
    /// </summary>
    public string Capability { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool name selected for the step.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the risk level for the planned tool call.
    /// </summary>
    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.Low;

    /// <summary>
    /// Gets or sets the summary of the intended input.
    /// </summary>
    public string InputSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the string arguments supplied to the tool.
    /// </summary>
    public IDictionary<string, string> Arguments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the JSON arguments supplied to the tool.
    /// </summary>
    public JsonElement? ArgumentsJson { get; set; }
}

/// <summary>
/// Represents a tool execution request prepared by the agent loop.
/// </summary>
public sealed class AgentToolExecutionRequest
{
    /// <summary>
    /// Initializes a new execution request.
    /// </summary>
    public AgentToolExecutionRequest(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        PlannedStep = plannedStep ?? throw new ArgumentNullException(nameof(plannedStep));
    }

    /// <summary>
    /// Gets the active loop context.
    /// </summary>
    public AgentLoopExecutionContext Context { get; }

    /// <summary>
    /// Gets the planned step to execute.
    /// </summary>
    public AgentPlannedStep PlannedStep { get; }
}

/// <summary>
/// Captures the outcome of a tool execution.
/// </summary>
public sealed class AgentToolExecutionResult
{
    /// <summary>
    /// Gets or sets the final execution status.
    /// </summary>
    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Succeeded;

    /// <summary>
    /// Gets or sets the associated trace identifier.
    /// </summary>
    public string? TraceId { get; set; }

    /// <summary>
    /// Gets or sets the tool exit code.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Gets or sets a short summary of the output.
    /// </summary>
    public string OutputSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the failure message, if any.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// Captures the result of inspecting tool output for the loop.
/// </summary>
public sealed class AgentResultInspection
{
    /// <summary>
    /// Gets or sets the loop decision after inspection.
    /// </summary>
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    /// <summary>
    /// Gets or sets the inspected output summary.
    /// </summary>
    public string OutputSummary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the final result, if inspection produced one.
    /// </summary>
    public string? FinalResult { get; set; }
}

/// <summary>
/// Captures validation output for a loop decision.
/// </summary>
public sealed class AgentLoopValidation
{
    /// <summary>
    /// Gets or sets the validation decision.
    /// </summary>
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    /// <summary>
    /// Gets or sets the validation message.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Represents an allow-or-deny decision for tool execution.
/// </summary>
public sealed class ToolExecutionPolicyDecision
{
    /// <summary>
    /// Gets or sets a value indicating whether the tool call is allowed.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the stable decision name.
    /// </summary>
    public string Decision { get; set; } = "denied";

    /// <summary>
    /// Gets or sets the human-readable reason for the decision.
    /// </summary>
    public string? Reason { get; set; }
}
