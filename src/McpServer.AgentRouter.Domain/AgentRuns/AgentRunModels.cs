namespace McpServer.AgentRouter.Domain.AgentRuns;

/// <summary>
/// Describes the input required to start or resume an agent run.
/// </summary>
public sealed class AgentRunRequest
{
    /// <summary>
    /// Gets or sets the model name to use for the run.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the user goal for the run.
    /// </summary>
    public string? Goal { get; set; }

    /// <summary>
    /// Gets or sets the system or assistant instructions for the run.
    /// </summary>
    public string? Instructions { get; set; }

    /// <summary>
    /// Gets or sets the sampling temperature.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Gets or sets the maximum token budget for the run.
    /// </summary>
    public int? MaxTokens { get; set; }
}

/// <summary>
/// Represents a persisted agent run and its artifacts.
/// </summary>
public sealed class AgentRun
{
    /// <summary>
    /// Gets or sets the unique identifier for the run.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the object type for the run payload.
    /// </summary>
    public string Object { get; set; } = "agent.run";

    /// <summary>
    /// Gets or sets the current run status.
    /// </summary>
    public string Status { get; set; } = AgentRunStatusNames.Queued;

    /// <summary>
    /// Gets or sets the model used for the run.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the run goal.
    /// </summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion timestamp, if the run has finished.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the final result text.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Gets or sets the terminal error details, if the run failed.
    /// </summary>
    public AgentRunError? Error { get; set; }

    /// <summary>
    /// Gets or sets the artifacts produced by the run.
    /// </summary>
    public IList<AgentRunArtifact> Artifacts { get; set; } = new List<AgentRunArtifact>();
}

/// <summary>
/// Describes a run artifact produced during execution.
/// </summary>
public sealed class AgentRunArtifact
{
    /// <summary>
    /// Gets or sets the artifact identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artifact type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artifact name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the artifact content.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Describes a terminal error for an agent run.
/// </summary>
public sealed class AgentRunError
{
    /// <summary>
    /// Gets or sets the human-readable error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stable error type name.
    /// </summary>
    public string Type { get; set; } = "agent_run_error";

    /// <summary>
    /// Gets or sets the stable error code.
    /// </summary>
    public string Code { get; set; } = "agent_run_failed";
}

/// <summary>
/// Provides stable string values for agent run states.
/// </summary>
public static class AgentRunStatusNames
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

/// <summary>
/// Provides stable artifact type names for agent runs.
/// </summary>
public static class AgentRunArtifactTypes
{
    /// <summary>Plan artifact type.</summary>
    public const string Plan = "plan";
    /// <summary>Generation artifact type.</summary>
    public const string Generation = "generation";
    /// <summary>Trace artifact type.</summary>
    public const string Trace = "trace";
}
