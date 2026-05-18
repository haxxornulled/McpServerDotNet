using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Services;
using McpServer.AgentRouter.Domain.Inference;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ModelRouterTests
{
    [Fact]
    public async Task CompleteAsync_UsesProviderName_InsteadOfClientTypeNameConvention()
    {
        var profile = new ModelProfile(
            name: "local-code",
            provider: "Ollama",
            model: "qwen3-coder:30b",
            baseUri: new Uri("http://127.0.0.1:11434/"),
            contextLength: 131072,
            maxOutputTokens: 32000,
            temperature: 0.15d,
            allowCloudProvider: false,
            allowNonLoopbackBaseUrl: false,
            timeoutSeconds: 900);

        var resolver = Substitute.For<IModelProfileResolver>();
        resolver.ResolveAsync("local-code", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelProfile>>(Fin<ModelProfile>.Succ(profile)));

        var client = Substitute.For<IChatModelClient>();
        client.ProviderName.Returns("Ollama");
        client.CompleteAsync(Arg.Any<ModelInvocationRequest>(), profile, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelTurnResult>>(Fin<ModelTurnResult>.Succ(new ModelTurnResult(
                provider: "Ollama",
                model: "qwen3-coder:30b",
                content: "router online",
                finishReason: "stop",
                promptTokens: 1,
                completionTokens: 2,
                elapsedMilliseconds: 3))));

        var router = new ModelRouter(
            resolver,
            new[] { client },
            NullLogger<ModelRouter>.Instance);

        var request = new ModelInvocationRequest(
            "local-code",
            new[] { new ChatTurnMessage("user", "ping") },
            null,
            null);

        var result = await router.CompleteAsync(request, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(turn => Assert.Equal("router online", turn.Content));
    }

    [Fact]
    public async Task CompleteAsync_Fails_WhenNoProviderClientIsRegistered()
    {
        var profile = new ModelProfile(
            name: "local-code",
            provider: "Ollama",
            model: "qwen3-coder:30b",
            baseUri: new Uri("http://127.0.0.1:11434/"),
            contextLength: 131072,
            maxOutputTokens: 32000,
            temperature: 0.15d,
            allowCloudProvider: false,
            allowNonLoopbackBaseUrl: false,
            timeoutSeconds: 900);

        var resolver = Substitute.For<IModelProfileResolver>();
        resolver.ResolveAsync("local-code", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelProfile>>(Fin<ModelProfile>.Succ(profile)));

        var router = new ModelRouter(
            resolver,
            global::System.Array.Empty<IChatModelClient>(),
            NullLogger<ModelRouter>.Instance);

        var request = new ModelInvocationRequest(
            "local-code",
            new[] { new ChatTurnMessage("user", "ping") },
            null,
            null);

        var result = await router.CompleteAsync(request, CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public async Task StreamAsync_UsesProviderName_InsteadOfClientTypeNameConvention()
    {
        var profile = new ModelProfile(
            name: "local-code",
            provider: "Ollama",
            model: "qwen3-coder:30b",
            baseUri: new Uri("http://127.0.0.1:11434/"),
            contextLength: 131072,
            maxOutputTokens: 32000,
            temperature: 0.15d,
            allowCloudProvider: false,
            allowNonLoopbackBaseUrl: false,
            timeoutSeconds: 900);

        var resolver = Substitute.For<IModelProfileResolver>();
        resolver.ResolveAsync("local-code", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelProfile>>(Fin<ModelProfile>.Succ(profile)));

        var client = Substitute.For<IChatModelClient>();
        client.ProviderName.Returns("Ollama");
        client.StreamAsync(Arg.Any<ModelInvocationRequest>(), profile, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ModelTurnStream>>(Fin<ModelTurnStream>.Succ(new ModelTurnStream(StreamChunks(
                "streamed response")))));

        var router = new ModelRouter(
            resolver,
            new[] { client },
            NullLogger<ModelRouter>.Instance);

        var request = new ModelInvocationRequest(
            "local-code",
            new[] { new ChatTurnMessage("user", "ping") },
            null,
            null);

        var result = await router.StreamAsync(request, CancellationToken.None);

        Assert.True(result.IsSucc);
        var stream = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected successful stream result."));

        var chunks = new List<ModelTurnChunk>();
        await foreach (var chunk in stream.Chunks)
        {
            chunks.Add(chunk);
        }

        Assert.Collection(chunks,
            item => Assert.Equal("streamed response", item.Content),
            item =>
            {
                Assert.True(item.IsFinal);
                Assert.Equal("stop", item.FinishReason);
            });
    }

    private static async IAsyncEnumerable<ModelTurnChunk> StreamChunks(string content)
    {
        yield return new ModelTurnChunk(content, isFinal: false);
        yield return new ModelTurnChunk(string.Empty, isFinal: true, "stop");
        await Task.CompletedTask;
    }
}
