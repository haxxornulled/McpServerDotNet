using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Inference;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Ollama;

public sealed class OllamaChatModelClient : IChatModelClient
{
    private const long InformationalCompletionThresholdMilliseconds = 2_000;
    private const long WarningCompletionThresholdMilliseconds = 10_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OllamaChatModelClient> _logger;

    public OllamaChatModelClient(
        IHttpClientFactory httpClientFactory,
        ILogger<OllamaChatModelClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "Ollama";

    public async ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);

        var validation = ValidateProfile(profile);
        if (validation.IsFail)
        {
            return validation.Match<Fin<ModelTurnResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected validation success."),
                Fail: error => error);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));

            var client = _httpClientFactory.CreateClient("agent-router-ollama");
            using var httpRequest = BuildRequest(request, profile, stream: false);

            var stopwatch = Stopwatch.StartNew();
            using var response = await client.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama chat request failed for profile {ProfileName}. StatusCode: {StatusCode}",
                    profile.Name,
                    (int)response.StatusCode);

                return Error.New($"Ollama returned HTTP {(int)response.StatusCode}: {TrimForError(body)}");
            }

            var parsed = ParseResponse(body);
            if (parsed.IsFail)
            {
                return parsed.Match<Fin<ModelTurnResult>>(
                    Succ: _ => throw new InvalidOperationException("Unexpected Ollama parse success."),
                    Fail: error => error);
            }

            var parsedResponse = parsed.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected Ollama parse failure."));

            LogCompletedRequest(profile, stopwatch.ElapsedMilliseconds);

        return Fin<ModelTurnResult>.Succ(new ModelTurnResult(
                provider: ProviderName,
                model: profile.Model,
                content: parsedResponse.Content,
                finishReason: parsedResponse.FinishReason,
                promptTokens: parsedResponse.PromptTokens,
                completionTokens: parsedResponse.CompletionTokens,
                elapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                toolCalls: parsedResponse.ToolCalls));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.New($"Ollama request timed out after {profile.TimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ollama provider was unavailable for profile {ProfileName}.", profile.Name);
            return Error.New($"Ollama provider unavailable at {profile.BaseUri.GetLeftPart(UriPartial.Authority)}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama chat request failed for profile {ProfileName}.", profile.Name);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(profile);

        var validation = ValidateProfile(profile);
        if (validation.IsFail)
        {
            return validation.Match<Fin<ModelTurnStream>>(
                Succ: _ => throw new InvalidOperationException("Unexpected validation success."),
                Fail: error => error);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));

            var client = _httpClientFactory.CreateClient("agent-router-ollama");
            using var httpRequest = BuildRequest(request, profile, stream: true);

            var response = await client.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

                _logger.LogWarning(
                    "Ollama streaming chat request failed for profile {ProfileName}. StatusCode: {StatusCode}",
                    profile.Name,
                    (int)response.StatusCode);

                response.Dispose();
                return Error.New($"Ollama returned HTTP {(int)response.StatusCode}: {TrimForError(body)}");
            }

            return Fin<ModelTurnStream>.Succ(new ModelTurnStream(
                StreamResponseAsync(response, timeout.Token)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.New($"Ollama request timed out after {profile.TimeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ollama provider was unavailable for profile {ProfileName}.", profile.Name);
            return Error.New($"Ollama provider unavailable at {profile.BaseUri.GetLeftPart(UriPartial.Authority)}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama chat request failed for profile {ProfileName}.", profile.Name);
            return Error.New(ex.Message);
        }
    }

    private void LogCompletedRequest(
        ModelProfile profile,
        long elapsedMilliseconds)
    {
        if (elapsedMilliseconds >= WarningCompletionThresholdMilliseconds)
        {
            _logger.LogWarning(
                "Ollama completed slowly for profile {ProfileName} model {Model} in {ElapsedMs}ms.",
                profile.Name,
                profile.Model,
                elapsedMilliseconds);
            return;
        }

        if (elapsedMilliseconds >= InformationalCompletionThresholdMilliseconds)
        {
            _logger.LogInformation(
                "Ollama completed profile {ProfileName} model {Model} in {ElapsedMs}ms.",
                profile.Name,
                profile.Model,
                elapsedMilliseconds);
            return;
        }

        _logger.LogDebug(
            "Ollama completed profile {ProfileName} model {Model} in {ElapsedMs}ms.",
            profile.Name,
            profile.Model,
            elapsedMilliseconds);
    }

    private static Fin<Unit> ValidateProfile(ModelProfile profile)
    {
        if (profile.BaseUri.Scheme is not "http" and not "https")
        {
            return Error.New("Ollama BaseUrl must use HTTP or HTTPS.");
        }

        if (!profile.AllowNonLoopbackBaseUrl && !profile.BaseUri.IsLoopback)
        {
            return Error.New("Ollama BaseUrl must be loopback unless AllowNonLoopbackBaseUrl is explicitly enabled.");
        }

        return Fin<Unit>.Succ(Unit.Default);
    }

    private static HttpRequestMessage BuildRequest(
        ModelInvocationRequest request,
        ModelProfile profile,
        bool stream)
    {
        var messages = request.Messages.Select(static message => new
        {
            role = message.Role,
            content = message.Content,
            tool_call_id = message.ToolCallId,
            tool_calls = message.ToolCalls is null
                ? null
                : message.ToolCalls.Select(static toolCall => new
                {
                    id = toolCall.Id,
                    type = "function",
                    function = new
                    {
                        name = toolCall.Name,
                        arguments = toolCall.Arguments.GetRawText()
                    }
                }).ToArray()
        }).ToArray();

        var options = new
        {
            temperature = Math.Clamp(request.Temperature ?? profile.Temperature, 0.0d, 2.0d),
            num_ctx = profile.ContextLength,
            num_predict = Math.Clamp(request.MaxOutputTokens ?? profile.MaxOutputTokens, 1, profile.MaxOutputTokens)
        };

        var tools = request.Tools is null || request.Tools.Count == 0
            ? null
            : request.Tools.Select(static tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = tool.InputSchema
                }
            }).ToArray();

        var payload = new
        {
            model = profile.Model,
            stream,
            messages,
            options,
            tools
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(profile.BaseUri, "api/chat"));
        httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("McpServer.AgentRouter", "0.1.0"));
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private static Fin<OllamaParsedResponse> ParseResponse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String)
        {
            return Error.New(error.GetString() ?? "Ollama returned an error.");
        }

        string? content = null;
        if (root.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var messageContent) &&
            messageContent.ValueKind == JsonValueKind.String)
        {
            content = messageContent.GetString();
        }
        else if (root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.String)
        {
            content = response.GetString();
        }

        if (content is null)
        {
            return Error.New("Ollama response did not contain message.content.");
        }

        var promptTokens = ReadInt32(root, "prompt_eval_count");
        var completionTokens = ReadInt32(root, "eval_count");
        var finishReason = ReadString(root, "done_reason") ?? "stop";
        var toolCalls = ReadToolCalls(root);

        return Fin<OllamaParsedResponse>.Succ(new OllamaParsedResponse(
            content,
            finishReason,
            promptTokens,
            completionTokens,
            toolCalls));
    }

    private static int ReadInt32(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static IReadOnlyList<ChatToolCall>? ReadToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("tool_calls", out var toolCallsElement) ||
            toolCallsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var toolCalls = new List<ChatToolCall>();
        foreach (var toolCallElement in toolCallsElement.EnumerateArray())
        {
            if (toolCallElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadString(toolCallElement, "id") ?? string.Empty;
            var type = ReadString(toolCallElement, "type");
            var function = toolCallElement.TryGetProperty("function", out var functionElement) &&
                functionElement.ValueKind == JsonValueKind.Object
                ? functionElement
                : default;

            var name = function.ValueKind == JsonValueKind.Object
                ? ReadString(function, "name") ?? string.Empty
                : string.Empty;

            var argumentsText = function.ValueKind == JsonValueKind.Object
                ? ReadString(function, "arguments") ?? "{}"
                : "{}";

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            JsonElement arguments;
            try
            {
                arguments = JsonDocument.Parse(argumentsText).RootElement.Clone();
            }
            catch (JsonException)
            {
                arguments = JsonDocument.Parse("{}").RootElement.Clone();
            }

            toolCalls.Add(new ChatToolCall(id, name, arguments));
        }

        return toolCalls.Count == 0 ? null : toolCalls;
    }

    private static bool ReadBoolean(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static string TrimForError(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private static async IAsyncEnumerable<ModelTurnChunk> StreamResponseAsync(
        HttpResponseMessage responseMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var response = responseMessage;
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(responseStream, Encoding.UTF8);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = ParseStreamChunk(line);
            if (chunk.IsFail)
            {
                throw new InvalidOperationException(chunk.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected stream parse success."),
                    Fail: error => error.Message));
            }

            var value = chunk.Match(
                Succ: item => item,
                Fail: _ => throw new InvalidOperationException("Unexpected stream parse failure."));

            if (value.Content.Length > 0)
            {
                yield return new ModelTurnChunk(value.Content, isFinal: false);
            }

            if (value.IsFinal)
            {
                yield return new ModelTurnChunk(string.Empty, isFinal: true, value.FinishReason);
                yield break;
            }
        }
    }

    private static Fin<OllamaStreamChunk> ParseStreamChunk(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                return Error.New(error.GetString() ?? "Ollama returned an error.");
            }

            var content = ReadMessageContent(root) ?? string.Empty;
            var finishReason = ReadString(root, "done_reason") ?? "stop";
            var isFinal = ReadBoolean(root, "done");

            return Fin<OllamaStreamChunk>.Succ(new OllamaStreamChunk(content, isFinal, finishReason));
        }
        catch (JsonException ex)
        {
            return Error.New($"Ollama streamed an invalid JSON chunk: {ex.Message}");
        }
    }

    private static string? ReadMessageContent(JsonElement root)
    {
        if (root.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var messageContent) &&
            messageContent.ValueKind == JsonValueKind.String)
        {
            return messageContent.GetString();
        }

        if (root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.String)
        {
            return response.GetString();
        }

        return null;
    }

    private sealed class OllamaParsedResponse
    {
        public OllamaParsedResponse(
            string content,
            string finishReason,
            int promptTokens,
            int completionTokens,
            IReadOnlyList<ChatToolCall>? toolCalls)
        {
            Content = content;
            FinishReason = finishReason;
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            ToolCalls = toolCalls;
        }

        public string Content { get; }

        public string FinishReason { get; }

        public int PromptTokens { get; }

        public int CompletionTokens { get; }

        public IReadOnlyList<ChatToolCall>? ToolCalls { get; }
    }

    private sealed class OllamaStreamChunk
    {
        public OllamaStreamChunk(
            string content,
            bool isFinal,
            string finishReason)
        {
            Content = content;
            IsFinal = isFinal;
            FinishReason = finishReason;
        }

        public string Content { get; }

        public bool IsFinal { get; }

        public string FinishReason { get; }
    }
}
