using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.AgentRuns;
using McpServer.AgentRouter.Application.Stores;
using McpServer.AgentRouter.Domain.AgentRuns;
using McpServer.AgentRouter.Domain.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AgentRunServiceTests
{
    [Fact]
    public async Task StartRunAsync_CompletesRun_AndStoresArtifacts()
    {
        var router = Substitute.For<IModelRouter>();
        router.CompleteAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelTurnResult>>(Fin<ModelTurnResult>.Succ(new ModelTurnResult(
                provider: "Ollama",
                model: "qwen2.5-coder:14b",
                content: "agent run complete",
                finishReason: "stop",
                promptTokens: 10,
                completionTokens: 3,
                elapsedMilliseconds: 123))));

        var store = new InMemoryAgentRunStore();
        var service = CreateService(router, store);

        var result = await service.StartRunAsync(new AgentRunRequest
        {
            Model = "fast-local",
            Goal = "Explain the router in one sentence."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected successful agent run."));

        Assert.StartsWith("run-", run.Id, StringComparison.Ordinal);
        Assert.Equal(AgentRunStatusNames.Completed, run.Status);
        Assert.Equal("fast-local", run.Model);
        Assert.Equal("agent run complete", run.Result);
        Assert.Null(run.Error);
        Assert.NotNull(run.CompletedAt);
        Assert.Contains(run.Artifacts, artifact => artifact.Type == "plan");
        Assert.Contains(run.Artifacts, artifact => artifact.Type == "generation");
        Assert.Contains(run.Artifacts, artifact => artifact.Type == "trace");

        var stored = await service.GetRunAsync(run.Id, CancellationToken.None);

        Assert.True(stored.IsSucc);
        stored.IfSucc(storedRun => Assert.Equal("agent run complete", storedRun.Result));
    }

    [Fact]
    public async Task StartRunAsync_UsesDefaultProfile_WhenModelIsMissing()
    {
        var router = Substitute.For<IModelRouter>();
        ModelInvocationRequest? capturedRequest = null;

        router.CompleteAsync(Arg.Do<ModelInvocationRequest>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelTurnResult>>(Fin<ModelTurnResult>.Succ(new ModelTurnResult(
                provider: "Ollama",
                model: "qwen3-coder:30b",
                content: "done",
                finishReason: "stop",
                promptTokens: 1,
                completionTokens: 1,
                elapsedMilliseconds: 1))));

        var service = CreateService(router, new InMemoryAgentRunStore());

        var result = await service.StartRunAsync(new AgentRunRequest
        {
            Goal = "Use the default profile."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        Assert.NotNull(capturedRequest);
        Assert.Equal("local-code", capturedRequest!.ModelProfileName);
    }

    [Fact]
    public async Task StartRunAsync_FailsValidation_WhenGoalIsMissing()
    {
        var service = CreateService(Substitute.For<IModelRouter>(), new InMemoryAgentRunStore());

        var result = await service.StartRunAsync(new AgentRunRequest
        {
            Model = "fast-local"
        }, CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public async Task StartRunAsync_StoresFailedRun_WhenModelRouterFails()
    {
        var router = Substitute.For<IModelRouter>();
        router.CompleteAsync(Arg.Any<ModelInvocationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelTurnResult>>(Error.New("provider unavailable")));

        var service = CreateService(router, new InMemoryAgentRunStore());

        var result = await service.StartRunAsync(new AgentRunRequest
        {
            Model = "fast-local",
            Goal = "This should create a failed run."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected stored failed run."));

        Assert.Equal(AgentRunStatusNames.Failed, run.Status);
        Assert.NotNull(run.Error);
        Assert.Equal("provider unavailable", run.Error!.Message);
        Assert.Contains(run.Artifacts, artifact => artifact.Type == "trace");
    }

    [Fact]
    public async Task GetRunAsync_Fails_WhenRunDoesNotExist()
    {
        var service = CreateService(Substitute.For<IModelRouter>(), new InMemoryAgentRunStore());

        var result = await service.GetRunAsync("run-missing", CancellationToken.None);

        Assert.True(result.IsFail);
    }

    private static AgentRunService CreateService(
        IModelRouter router,
        IAgentRunStore store)
    {
        return new AgentRunService(
            router,
            store,
            TestRuntimeSettings.Create(),
            NullLogger<AgentRunService>.Instance);
    }
}
