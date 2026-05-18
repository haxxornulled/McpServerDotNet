using System.Net;
using System.Text;
using System.Text.Json;
using McpServer.Application.Inference;
using McpServer.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class OllamaInferenceServiceTests
{
    [Fact]
    public async Task CompleteAsync_Should_Send_128k_Context_And_NumPredict_To_Ollama()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = CreateSut(handler, new OllamaInferenceOptions(
            enabled: true,
            baseUrl: "http://127.0.0.1:11434",
            defaultModel: "qwen3-coder:30b",
            allowedModels: ["qwen3-coder:30b", "qwen2.5-coder:14b"],
            timeoutSeconds: 120,
            maxTimeoutSeconds: 900,
            maxPromptChars: 500_000,
            maxOutputChars: 32_000,
            contextLength: 131_072,
            numPredict: 32_000,
            temperature: 0.15d,
            allowNonLoopbackBaseUrl: false));

        var result = await sut.CompleteAsync(new LocalInferenceRequest(
            operation: "plan",
            prompt: "Plan a safe refactor.",
            systemPrompt: "You are a local coding assistant.",
            model: null,
            temperature: null,
            maxOutputChars: null,
            timeoutSeconds: null), CancellationToken.None);

        Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message));

        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("http://127.0.0.1:11434/api/chat", handler.RequestUri?.ToString());
        Assert.NotNull(handler.RequestBody);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;

        Assert.Equal("qwen3-coder:30b", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var options = root.GetProperty("options");
        Assert.Equal(131_072, options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(32_000, options.GetProperty("num_predict").GetInt32());
        Assert.Equal(0.15d, options.GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task CompleteAsync_Should_Block_NonLoopback_BaseUrl_By_Default()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler, new OllamaInferenceOptions(
            enabled: true,
            baseUrl: "http://192.168.1.50:11434",
            defaultModel: "qwen3-coder:30b",
            allowedModels: ["qwen3-coder:30b"],
            timeoutSeconds: 120,
            maxTimeoutSeconds: 900,
            maxPromptChars: 500_000,
            maxOutputChars: 32_000,
            contextLength: 131_072,
            numPredict: 32_000,
            temperature: 0.15d,
            allowNonLoopbackBaseUrl: false));

        var result = await sut.CompleteAsync(new LocalInferenceRequest(
            operation: "complete",
            prompt: "hello",
            systemPrompt: null,
            model: null,
            temperature: null,
            maxOutputChars: null,
            timeoutSeconds: null), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(0, handler.SendCount);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected validation failure."),
            Fail: failure => failure.Message);
        Assert.Contains("loopback", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_Should_Block_Model_Switching_When_AllowedModels_Is_Empty()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler, new OllamaInferenceOptions(
            enabled: true,
            baseUrl: "http://127.0.0.1:11434",
            defaultModel: "qwen3-coder:30b",
            allowedModels: [],
            timeoutSeconds: 120,
            maxTimeoutSeconds: 900,
            maxPromptChars: 500_000,
            maxOutputChars: 32_000,
            contextLength: 131_072,
            numPredict: 32_000,
            temperature: 0.15d,
            allowNonLoopbackBaseUrl: false));

        var result = await sut.CompleteAsync(new LocalInferenceRequest(
            operation: "complete",
            prompt: "hello",
            systemPrompt: null,
            model: "qwen2.5-coder:14b",
            temperature: null,
            maxOutputChars: null,
            timeoutSeconds: null), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(0, handler.SendCount);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected model validation failure."),
            Fail: failure => failure.Message);
        Assert.Contains("not allowed", error, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task CompleteAsync_Should_Parse_Legacy_Response_Field()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "response": "legacy response"
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello"), CancellationToken.None);

        Assert.True(result.IsSucc, GetError(result));
        var value = result.Match(
            Succ: response => response,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal("legacy response", value.Response);
    }

    [Fact]
    public async Task CompleteAsync_Should_Truncate_Response_To_Request_MaxOutputChars()
    {
        var responseText = new string('a', 300);
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {
                  "message": {
                    "role": "assistant",
                    "content": "{{responseText}}"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });

        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello", maxOutputChars: 256), CancellationToken.None);

        Assert.True(result.IsSucc, GetError(result));
        var value = result.Match(
            Succ: response => response,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.True(value.Truncated);
        Assert.Equal(new string('a', 256), value.Response);
        Assert.Equal(256, value.ResponseChars);
    }

    [Fact]
    public async Task CompleteAsync_Should_Reject_Blank_Prompt_Before_Http_Call()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "   "), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(0, handler.SendCount);
        Assert.Contains("Prompt is required", GetError(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_Should_Reject_Too_Large_Prompt_Before_Http_Call()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler, new OllamaInferenceOptions(
            enabled: true,
            baseUrl: "http://127.0.0.1:11434",
            defaultModel: "qwen3-coder:30b",
            allowedModels: ["qwen3-coder:30b"],
            timeoutSeconds: 120,
            maxTimeoutSeconds: 900,
            maxPromptChars: 1024,
            maxOutputChars: 32_000,
            contextLength: 131_072,
            numPredict: 32_000,
            temperature: 0.15d,
            allowNonLoopbackBaseUrl: false));

        var result = await sut.CompleteAsync(CreateRequest("complete", new string('x', 1025)), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(0, handler.SendCount);
        Assert.Contains("Prompt exceeded", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_Should_Reject_Timeout_Above_Configured_Maximum()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello", timeoutSeconds: 901), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Equal(0, handler.SendCount);
        Assert.Contains("timeoutSeconds", GetError(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_Should_Use_Requested_Allowed_Model_Case_Insensitively()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello", model: "QWEN2.5-CODER:14B"), CancellationToken.None);

        Assert.True(result.IsSucc, GetError(result));
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("QWEN2.5-CODER:14B", document.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteAsync_Should_Clamp_Request_Temperature_In_Payload()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "message": {
                    "role": "assistant",
                    "content": "ok"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello", temperature: 12.5d), CancellationToken.None);

        Assert.True(result.IsSucc, GetError(result));
        using var document = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(2.0d, document.RootElement.GetProperty("options").GetProperty("temperature").GetDouble(), 3);
    }

    [Fact]
    public async Task CompleteAsync_Should_Return_Failure_When_Response_Has_No_Content_Field()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello"), CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Contains("message.content", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_Should_Surface_Http_Error_Body()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("bad model", Encoding.UTF8, "text/plain")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.CompleteAsync(CreateRequest("complete", "hello"), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = GetError(result);
        Assert.Contains("HTTP 400", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad model", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_Should_Parse_Model_Names()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "models": [
                    { "name": "qwen3-coder:30b" },
                    { "name": "qwen2.5-coder:14b" }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSucc, GetStatusError(result));
        var value = result.Match(
            Succ: status => status,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.True(value.ServerReachable);
        Assert.Contains("qwen3-coder:30b", value.AvailableModels);
        Assert.Contains("qwen2.5-coder:14b", value.AvailableModels);
    }

    [Fact]
    public async Task GetStatusAsync_Should_Report_Unreachable_On_Http_Error()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("down", Encoding.UTF8, "text/plain")
        });
        var sut = CreateSut(handler, CreateDefaultOptions());

        var result = await sut.GetStatusAsync(CancellationToken.None);

        Assert.True(result.IsSucc, GetStatusError(result));
        var value = result.Match(
            Succ: status => status,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.False(value.ServerReachable);
        Assert.NotNull(value.Message);
        Assert.Contains("HTTP 503", value.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static OllamaInferenceOptions CreateDefaultOptions()
    {
        return new OllamaInferenceOptions(
            enabled: true,
            baseUrl: "http://127.0.0.1:11434",
            defaultModel: "qwen3-coder:30b",
            allowedModels: ["qwen3-coder:30b", "qwen2.5-coder:14b"],
            timeoutSeconds: 120,
            maxTimeoutSeconds: 900,
            maxPromptChars: 500_000,
            maxOutputChars: 32_000,
            contextLength: 131_072,
            numPredict: 32_000,
            temperature: 0.15d,
            allowNonLoopbackBaseUrl: false);
    }

    private static LocalInferenceRequest CreateRequest(
        string operation,
        string prompt,
        string? systemPrompt = null,
        string? model = null,
        double? temperature = null,
        int? maxOutputChars = null,
        int? timeoutSeconds = null)
    {
        return new LocalInferenceRequest(
            operation: operation,
            prompt: prompt,
            systemPrompt: systemPrompt,
            model: model,
            temperature: temperature,
            maxOutputChars: maxOutputChars,
            timeoutSeconds: timeoutSeconds);
    }

    private static string GetError(LanguageExt.Fin<LocalInferenceResponse> result)
    {
        return result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message);
    }

    private static string GetStatusError(LanguageExt.Fin<LocalInferenceStatus> result)
    {
        return result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message);
    }

    private static OllamaInferenceService CreateSut(
        CapturingHttpMessageHandler handler,
        OllamaInferenceOptions options)
    {
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var factory = new FixedHttpClientFactory(httpClient);
        return new OllamaInferenceService(factory, options, NullLogger<OllamaInferenceService>.Instance);
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

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public int SendCount { get; private set; }

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return _response;
        }
    }
}
