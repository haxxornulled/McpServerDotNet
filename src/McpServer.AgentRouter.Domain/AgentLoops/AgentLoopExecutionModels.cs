using System.Text.Json;

namespace McpServer.AgentRouter.Domain.AgentLoops;

public sealed class AgentLoopExecutionContext
{
    private readonly string[] _allowedCapabilities;

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

    public AgentLoopRun Run { get; }

    public AgentLoopRequest Request { get; }

    public int MaxSteps { get; }

    public int MaxToolCalls { get; }

    public int MaxRuntimeSeconds { get; }

    public IReadOnlyList<string> AllowedCapabilities => _allowedCapabilities;

    public int CompletedStepCount => Run.Steps.Count;
}

public sealed class AgentPlannedStep
{
    public AgentStepPhase Phase { get; set; } = AgentStepPhase.Observe;

    public string Capability { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.Low;

    public string InputSummary { get; set; } = string.Empty;

    public IDictionary<string, string> Arguments { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public JsonElement? ArgumentsJson { get; set; }
}

public sealed class AgentToolExecutionRequest
{
    public AgentToolExecutionRequest(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        PlannedStep = plannedStep ?? throw new ArgumentNullException(nameof(plannedStep));
    }

    public AgentLoopExecutionContext Context { get; }

    public AgentPlannedStep PlannedStep { get; }
}

public sealed class AgentToolExecutionResult
{
    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Succeeded;

    public string? TraceId { get; set; }

    public int ExitCode { get; set; }

    public string OutputSummary { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

public sealed class AgentResultInspection
{
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    public string OutputSummary { get; set; } = string.Empty;

    public string? FinalResult { get; set; }
}

public sealed class AgentLoopValidation
{
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    public string? Message { get; set; }
}

public sealed class ToolExecutionPolicyDecision
{
    public bool Allowed { get; set; }

    public string Decision { get; set; } = "denied";

    public string? Reason { get; set; }
}
