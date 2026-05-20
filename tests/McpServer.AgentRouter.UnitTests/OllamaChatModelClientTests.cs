using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Inference;
using McpServer.AgentRouter.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Http;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class OllamaChatModelClientTests
{
    [Fact]
    public async Task StreamAsync_Should_Send_Stream_True_AndYieldChunks()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"message":{"role":"assistant","content":"Hel"},"done":false}
                {"message":{"role":"assistant","content":"lo"},"done":false}
                {"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop","prompt_eval_count":4,"eval_count":2}
                """,
                Encoding.UTF8,
                "application/x-ndjson")
        });

        var sut = CreateSut(handler);

        var result = await sut.StreamAsync(CreateRequest(), CreateProfile(), CancellationToken.None);

        Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message));

        var stream = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected streaming success."));

        var chunks = new List<ModelTurnChunk>();
        await foreach (var chunk in stream.Chunks)
        {
            chunks.Add(chunk);
        }

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hel", chunks[0].Content);
        Assert.False(chunks[0].IsFinal);
        Assert.Equal("lo", chunks[1].Content);
        Assert.False(chunks[1].IsFinal);
        Assert.True(chunks[2].IsFinal);
        Assert.Equal("stop", chunks[2].FinishReason);

        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("http://127.0.0.1:11434/api/chat", handler.RequestUri?.ToString());
        Assert.Contains("\"stream\":true", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamAsync_Allows_Concurrent_Requests_Without_Shared_State()
    {
        var handler = new CoordinatedHttpMessageHandler(
            new[]
            {
                CreateStreamingResponse("first"),
                CreateStreamingResponse("second")
            });

        var sut = CreateSut(handler);
        var profile = CreateProfile();

        var firstTask = sut.StreamAsync(CreateRequest("first"), profile, CancellationToken.None).AsTask();
        var secondTask = sut.StreamAsync(CreateRequest("second"), profile, CancellationToken.None).AsTask();

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, result => Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message)));

        var firstStream = results[0].Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected first stream success."));
        var secondStream = results[1].Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected second stream success."));

        var firstChunks = await CollectChunksAsync(firstStream.Chunks);
        var secondChunks = await CollectChunksAsync(secondStream.Chunks);

        var contents = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            firstChunks[0].Content,
            secondChunks[0].Content
        };

        Assert.Equal(new[] { "first", "second" }, contents.Order(StringComparer.Ordinal).ToArray());
        Assert.True(firstChunks[^1].IsFinal);
        Assert.True(secondChunks[^1].IsFinal);

        Assert.Equal(2, handler.SendCount);
        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.All(handler.RequestBodies, body =>
            Assert.Contains("\"stream\":true", body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompleteAsync_Should_Send_Tools_And_Parse_Tool_Calls()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"message":{"role":"assistant","content":"","tool_calls":[{"id":"call-1","type":"function","function":{"name":"web.search","arguments":"{\"query\":\"AgentRouter\"}"}}]},"done":true,"done_reason":"stop","prompt_eval_count":5,"eval_count":3}
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = CreateSut(handler);
        var request = new ModelInvocationRequest(
            modelProfileName: "local-code",
            messages: new[] { new ChatTurnMessage("user", "Search for AgentRouter") },
            temperature: null,
            maxOutputTokens: null,
            tools: new[]
            {
                new ChatToolDefinition(
                    "web.search",
                    "Searches the web for a query and returns ranked results.",
                    JsonSerializer.SerializeToElement(new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new
                        {
                            query = new { type = "string" }
                        },
                        required = new[] { "query" }
                    }))
            });

        var result = await sut.CompleteAsync(request, CreateProfile(), CancellationToken.None);

        Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message));

        var turn = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected completion success."));

        Assert.Single(turn.ToolCalls!);
        Assert.Equal("call-1", turn.ToolCalls![0].Id);
        Assert.Equal("web.search", turn.ToolCalls![0].Name);
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Contains("\"tools\"", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("web.search", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaChatModelClient CreateSut(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var factory = new FixedHttpClientFactory(httpClient);

        return new OllamaChatModelClient(factory, NullLogger<OllamaChatModelClient>.Instance);
    }

    private static ModelInvocationRequest CreateRequest(string prompt = "ping")
    {
        return new ModelInvocationRequest(
            modelProfileName: "local-code",
            messages: new[] { new ChatTurnMessage("user", prompt) },
            temperature: null,
            maxOutputTokens: null);
    }

    private static ModelProfile CreateProfile()
    {
        return new ModelProfile(
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
    }

    private static HttpResponseMessage CreateStreamingResponse(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {"message":{"role":"assistant","content":"{{content}}"},"done":false}
                {"message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}
                """,
                Encoding.UTF8,
                "application/x-ndjson")
        };
    }

    private static async Task<List<ModelTurnChunk>> CollectChunksAsync(IAsyncEnumerable<ModelTurnChunk> chunks)
    {
        var values = new List<ModelTurnChunk>();
        await foreach (var chunk in chunks)
        {
            values.Add(chunk);
        }

        return values;
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public string RequestBody { get; private set; } = string.Empty;

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return _response;
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public FixedHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            return _httpClient;
        }
    }

    private sealed class CoordinatedHttpMessageHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<HttpResponseMessage> _responses;
        private readonly TaskCompletionSource<bool> _firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _sendCount;

        public CoordinatedHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            _responses = new ConcurrentQueue<HttpResponseMessage>(responses);
        }

        public int SendCount => _sendCount;

        public ConcurrentBag<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var order = Interlocked.Increment(ref _sendCount);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (order == 1)
            {
                _firstStarted.SetResult(true);
                await _secondStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (order == 2)
            {
                _secondStarted.SetResult(true);
                await _firstStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException("No queued response remained.");
            }

            return response;
        }
    }
}
