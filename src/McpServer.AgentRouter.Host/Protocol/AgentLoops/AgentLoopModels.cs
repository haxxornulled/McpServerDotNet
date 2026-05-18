using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.AgentRouter.Host.Protocol.AgentLoops;

public sealed class AgentLoopRequest
{
    [JsonPropertyName("goal")]
    public string? Goal { get; set; }

    [JsonPropertyName("max_steps")]
    public int? MaxSteps { get; set; }

    [JsonPropertyName("allowed_capabilities")]
    public IList<string> AllowedCapabilities { get; set; } = new List<string>();

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }
}

public sealed class AgentLoopRun
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "agent.loop_run";

    [JsonPropertyName("status")]
    public string Status { get; set; } = AgentLoopStatusNames.Queued;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("error")]
    public AgentLoopError? Error { get; set; }

    [JsonPropertyName("steps")]
    public IList<AgentLoopStep> Steps { get; set; } = new List<AgentLoopStep>();
}

public sealed class AgentLoopStep
{
    [JsonPropertyName("step_id")]
    public string StepId { get; set; } = string.Empty;

    [JsonPropertyName("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("phase")]
    public AgentStepPhase Phase { get; set; } = AgentStepPhase.Observe;

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = string.Empty;

    [JsonPropertyName("tool_name")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("risk_level")]
    public ToolRiskLevel RiskLevel { get; set; } = ToolRiskLevel.Low;

    [JsonPropertyName("status")]
    public ToolExecutionStatus Status { get; set; } = ToolExecutionStatus.Pending;

    [JsonPropertyName("decision")]
    public AgentDecisionType Decision { get; set; } = AgentDecisionType.Continue;

    [JsonPropertyName("policy_decision")]
    public string PolicyDecision { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("input_summary")]
    public string InputSummary { get; set; } = string.Empty;

    [JsonPropertyName("output_summary")]
    public string OutputSummary { get; set; } = string.Empty;

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    [JsonPropertyName("elapsed_milliseconds")]
    public long ElapsedMilliseconds { get; set; }
}

public sealed class AgentLoopError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "agent_loop_error";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "agent_loop_failed";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentDecisionType
{
    Continue,
    Retry,
    Repair,
    Escalate,
    StopSucceeded,
    StopFailed
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToolRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter))]
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
