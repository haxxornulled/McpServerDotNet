namespace McpServer.AgentRouter.Domain.AgentRuns;

public sealed class AgentRunState
{
    private AgentRunState(AgentRun run)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public AgentRun Run { get; }

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

    public void Touch()
    {
        Run.UpdatedAt = DateTimeOffset.UtcNow;
    }

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
