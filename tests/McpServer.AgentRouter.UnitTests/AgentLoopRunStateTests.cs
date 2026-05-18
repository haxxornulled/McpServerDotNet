using McpServer.AgentRouter.Domain.AgentLoops;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AgentLoopRunStateTests
{
    [Fact]
    public void Start_CreatesRunningRun_WithTrimmedGoal()
    {
        var state = AgentLoopRunState.Start("  inspect workspace  ");

        Assert.Equal(AgentLoopStatusNames.Running, state.Run.Status);
        Assert.Equal("inspect workspace", state.Run.Goal);
        Assert.NotEmpty(state.Run.Id);
        Assert.NotEqual(default, state.Run.CreatedAt);
        Assert.Equal(state.Run.CreatedAt, state.Run.UpdatedAt);
    }

    [Fact]
    public void BeginStep_SeedsPendingStep_AndAddsItToRun()
    {
        var state = AgentLoopRunState.Start("inspect workspace");

        var step = state.BeginStep(1, new AgentPlannedStep
        {
            Phase = AgentStepPhase.Act,
            Capability = "mcp.tools.call",
            ToolName = "fs.list_directory",
            RiskLevel = ToolRiskLevel.Low,
            InputSummary = "list files"
        });

        Assert.Single(state.Run.Steps);
        Assert.Equal(1, step.Sequence);
        Assert.Equal(ToolExecutionStatus.Pending, step.Status);
        Assert.Equal(AgentDecisionType.Continue, step.Decision);
        Assert.Equal("fs.list_directory", step.ToolName);
    }

    [Fact]
    public void CompleteExecutedStep_UsesFallbackElapsed_WhenResultElapsedIsZero()
    {
        var state = AgentLoopRunState.Start("inspect workspace");
        var step = state.BeginStep(1, new AgentPlannedStep
        {
            Phase = AgentStepPhase.Act,
            Capability = "mcp.tools.call",
            ToolName = "fs.list_directory",
            RiskLevel = ToolRiskLevel.Low,
            InputSummary = "list files"
        });

        state.CompleteExecutedStep(step, new AgentToolExecutionResult
        {
            Status = ToolExecutionStatus.Succeeded,
            OutputSummary = "ok",
            ElapsedMilliseconds = 0
        }, 42);

        Assert.Equal(ToolExecutionStatus.Succeeded, step.Status);
        Assert.Equal("ok", step.OutputSummary);
        Assert.Equal(42, step.ElapsedMilliseconds);
        Assert.NotNull(step.CompletedAt);
    }

    [Fact]
    public void ApplyInspection_UpdatesDecision_AndOutputSummary()
    {
        var state = AgentLoopRunState.Start("inspect workspace");
        var step = state.BeginStep(1, new AgentPlannedStep
        {
            Phase = AgentStepPhase.Act,
            Capability = "mcp.tools.call",
            ToolName = "fs.list_directory",
            RiskLevel = ToolRiskLevel.Low,
            InputSummary = "list files"
        });

        state.ApplyInspection(step, new AgentResultInspection
        {
            Decision = AgentDecisionType.StopSucceeded,
            OutputSummary = "done"
        });

        Assert.Equal(AgentDecisionType.StopSucceeded, step.Decision);
        Assert.Equal("done", step.OutputSummary);
    }

    [Fact]
    public void MarkFailed_SetsTerminalFailureState()
    {
        var state = AgentLoopRunState.Start("inspect workspace");

        state.MarkFailed("tool failed", "tool_execution_failed");

        Assert.Equal(AgentLoopStatusNames.Failed, state.Run.Status);
        Assert.Null(state.Run.Result);
        Assert.NotNull(state.Run.Error);
        Assert.Equal("tool_execution_failed", state.Run.Error!.Code);
        Assert.Equal("tool failed", state.Run.Error.Message);
        Assert.NotNull(state.Run.CompletedAt);
    }

    [Fact]
    public void MarkCompleted_SetsTerminalSuccessState()
    {
        var state = AgentLoopRunState.Start("inspect workspace");

        state.MarkCompleted("directory listed");

        Assert.Equal(AgentLoopStatusNames.Completed, state.Run.Status);
        Assert.Equal("directory listed", state.Run.Result);
        Assert.Null(state.Run.Error);
        Assert.NotNull(state.Run.CompletedAt);
    }
}
