namespace McpServer.AgentRouter.Domain.AgentRuns;

/// <summary>
/// Owns the mutable state transitions for an agent run.
/// </summary>
public sealed class AgentRunState
{
    /// <summary>
    /// Initializes a new run state wrapper.
    /// </summary>
    private AgentRunState(AgentRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    /// <summary>
    /// Gets the backing run model.
    /// </summary>
    public AgentRun Run { get; }

    /// <summary>
    /// Starts a new run for the supplied model and goal.
    /// </summary>
    public static AgentRunState Start(
        string model,
        string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);

        var now = DateTimeOffset.UtcNow;
        var run = new AgentRun
        {
            Id = "run-" + Guid.NewGuid().ToString("N"),
            Status = AgentRunStatusNames.Running,
            Model = model.Trim(),
            Goal = goal.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        return new AgentRunState(run);
    }

    /// <summary>
    /// Updates the run timestamp without changing terminal state.
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
        Run.Status = AgentRunStatusNames.Completed;
        Run.Result = result;
        Run.Error = null;
        Run.UpdatedAt = now;
        Run.CompletedAt = now;
    }

    /// <summary>
    /// Marks the run as failed with a stable error payload.
    /// </summary>
    public void MarkFailed(
        string errorMessage,
        string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        var now = DateTimeOffset.UtcNow;
        Run.Status = AgentRunStatusNames.Failed;
        Run.Result = null;
        Run.Error = new AgentRunError
        {
            Message = errorMessage,
            Type = "agent_run_error",
            Code = errorCode
        };
        Run.UpdatedAt = now;
        Run.CompletedAt = now;
    }

    /// <summary>
    /// Adds a new artifact to the run.
    /// </summary>
    public AgentRunArtifact AddArtifact(
        string type,
        string name,
        string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);

        var artifact = new AgentRunArtifact
        {
            Id = "artifact-" + Guid.NewGuid().ToString("N"),
            Type = type.Trim(),
            Name = name.Trim(),
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };

        Run.Artifacts.Add(artifact);
        Run.UpdatedAt = artifact.CreatedAt;
        return artifact;
    }
}
