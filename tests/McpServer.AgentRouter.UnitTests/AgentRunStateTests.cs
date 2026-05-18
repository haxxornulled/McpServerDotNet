using McpServer.AgentRouter.Domain.AgentRuns;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AgentRunStateTests
{
    [Fact]
    public void Start_CreatesRunningRun_WithTrimmedInputs()
    {
        var state = AgentRunState.Start(" fast-local ", "  summarize workspace  ");

        Assert.Equal(AgentRunStatusNames.Running, state.Run.Status);
        Assert.Equal("fast-local", state.Run.Model);
        Assert.Equal("summarize workspace", state.Run.Goal);
        Assert.StartsWith("run-", state.Run.Id, StringComparison.Ordinal);
        Assert.Equal(state.Run.CreatedAt, state.Run.UpdatedAt);
    }

    [Fact]
    public void AddArtifact_AppendsArtifact_AndTouchesRun()
    {
        var state = AgentRunState.Start("fast-local", "summarize workspace");

        var artifact = state.AddArtifact(
            AgentRunArtifactTypes.Plan,
            AgentRunArtifactTypes.Plan,
            "do the work");

        Assert.Single(state.Run.Artifacts);
        Assert.Equal(AgentRunArtifactTypes.Plan, artifact.Type);
        Assert.Equal("do the work", artifact.Content);
        Assert.Equal(artifact.CreatedAt, state.Run.UpdatedAt);
    }

    [Fact]
    public void MarkCompleted_SetsTerminalSuccessState()
    {
        var state = AgentRunState.Start("fast-local", "summarize workspace");

        state.MarkCompleted("finished");

        Assert.Equal(AgentRunStatusNames.Completed, state.Run.Status);
        Assert.Equal("finished", state.Run.Result);
        Assert.Null(state.Run.Error);
        Assert.NotNull(state.Run.CompletedAt);
    }

    [Fact]
    public void MarkFailed_SetsTerminalFailureState()
    {
        var state = AgentRunState.Start("fast-local", "summarize workspace");

        state.MarkFailed("provider unavailable", "agent_run_failed");

        Assert.Equal(AgentRunStatusNames.Failed, state.Run.Status);
        Assert.Null(state.Run.Result);
        Assert.NotNull(state.Run.Error);
        Assert.Equal("provider unavailable", state.Run.Error!.Message);
        Assert.Equal("agent_run_failed", state.Run.Error.Code);
        Assert.NotNull(state.Run.CompletedAt);
    }
}
