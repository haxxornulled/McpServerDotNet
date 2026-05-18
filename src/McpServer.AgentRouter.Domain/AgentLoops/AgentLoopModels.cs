using System.Text.Json;

namespace McpServer.AgentRouter.Domain.AgentLoops;

public sealed class AgentLoopRequest
{
    public string? Goal { get; set; }

    public int? MaxSteps { get; set; }

    public IList<string> AllowedCapabilities { get; set; } = new List<string>();

    public string? ToolName { get; set; }

    public JsonElement? Arguments { get; set; }
}

public sealed class AgentLoopRun
{
    public string Id { get; set; } = string.Empty;

    public string Status { get; set; } = AgentLoopStatusNames.Queued;

    public string Goal { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Result { get; set; }

    public AgentLoopError? Error { get; set; }

    public IList<AgentLoopStep> Steps { get; set; } = new List<AgentLoopStep>();
}

public sealed class AgentLoopStep
{
    public string StepId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public AgentStepPhase Phase { get; set; } = AgentStepPhase.Observe;

    public string Capability { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.Low;

    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Pending;

    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    public string PolicyDecision { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string InputSummary { get; set; } = string.Empty;

    public string OutputSummary { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public string? TraceId { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

public sealed class AgentLoopError
{
    public string Message { get; set; } = string.Empty;

    public string Type { get; set; } = "agent_loop_error";

    public string Code { get; set; } = "agent_loop_failed";
}

public enum AgentStepPhase
{
    Observe,
    Hypothesize,
    Plan,
    Act,
    Validate,
    Complete,
    Failed
}

public enum AgentDecisionType
{
    Continue,
    Retry,
    Repair,
    Escalate,
    StopSucceeded,
    StopFailed
}

public enum ToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum ToolExecutionStatus
{
    Pending,
    Skipped,
    Running,
    Succeeded,
    Failed,
    Denied
}

public static class AgentLoopStatusNames
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
