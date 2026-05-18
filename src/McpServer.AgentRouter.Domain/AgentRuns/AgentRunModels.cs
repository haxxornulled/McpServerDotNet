namespace McpServer.AgentRouter.Domain.AgentRuns;

public sealed class AgentRunRequest
{
    public string? Model { get; set; }

    public string? Goal { get; set; }

    public string? Instructions { get; set; }

    public double? Temperature { get; set; }

    public int? MaxTokens { get; set; }
}

public sealed class AgentRun
{
    public string Id { get; set; } = string.Empty;

    public string Object { get; set; } = "agent.run";

    public string Status { get; set; } = AgentRunStatusNames.Queued;

    public string Model { get; set; } = string.Empty;

    public string Goal { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Result { get; set; }

    public AgentRunError? Error { get; set; }

    public IList<AgentRunArtifact> Artifacts { get; set; } = new List<AgentRunArtifact>();
}

public sealed class AgentRunArtifact
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AgentRunError
{
    public string Message { get; set; } = string.Empty;

    public string Type { get; set; } = "agent_run_error";

    public string Code { get; set; } = "agent_run_failed";
}

public static class AgentRunStatusNames
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class AgentRunArtifactTypes
{
    public const string Plan = "plan";
    public const string Generation = "generation";
    public const string Trace = "trace";
}
