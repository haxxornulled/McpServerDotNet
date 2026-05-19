using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Inference;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Ollama;

public sealed class OllamaInferenceService : ILocalInferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OllamaInferenceOptions _options;
    private readonly ILogger<OllamaInferenceService> _logger;
    private readonly Uri _baseUri;

    public OllamaInferenceService(
        IHttpClientFactory httpClientFactory,
        OllamaInferenceOptions options,
        ILogger<OllamaInferenceService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baseUri = CreateBaseUri(options.BaseUrl);
    }

    public async ValueTask<Fin<LocalInferenceResponse>> CompleteAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateBaseUri();
        if (validation.IsFail)
        {
            return PropagateFailure<LocalInferenceResponse>(validation);
        }

        validation = ValidateRequest(request);
        if (validation.IsFail)
        {
            return PropagateFailure<LocalInferenceResponse>(validation);
        }

        var model = ResolveModel(request.Model, out var modelError);
        if (modelError is not null)
        {
            return Error.New(modelError);
        }

        var timeoutSeconds = ClampTimeout(request.TimeoutSeconds);
        var maxOutputChars = ClampMaxOutputChars(request.MaxOutputChars);
        var temperature = request.Temperature is null
            ? _options.Temperature
            : Math.Clamp(request.Temperature.Value, 0.0d, 2.0d);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var httpClient = _httpClientFactory.CreateClient("ollama");
            using var httpRequest = BuildChatRequest(model, request, temperature);

            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Ollama request failed for operation {Operation}. StatusCode: {StatusCode}",
                    request.Operation,
                    (int)response.StatusCode);

                return Error.New($"Ollama returned HTTP {(int)response.StatusCode}: {TrimForError(body)}");
            }

            var content = ExtractAssistantContent(body);
            if (content is null)
            {
                return Error.New("Ollama response did not contain message.content.");
            }

            var truncated = false;
            if (content.Length > maxOutputChars)
            {
                content = content[..maxOutputChars];
                truncated = true;
            }

            _logger.LogInformation(
                "Ollama completed operation {Operation} with model {Model} in {ElapsedMs}ms",
                request.Operation,
                model,
                stopwatch.ElapsedMilliseconds);

            return Fin<LocalInferenceResponse>.Succ(new LocalInferenceResponse(
                provider: "ollama",
                model: model,
                operation: request.Operation,
                response: content,
                promptChars: request.Prompt.Length + (request.SystemPrompt?.Length ?? 0),
                responseChars: content.Length,
                truncated: truncated,
                elapsedMilliseconds: stopwatch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error.New($"Ollama request timed out after {timeoutSeconds} seconds.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama request failed for operation {Operation}", request.Operation);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<LocalInferenceStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var validation = ValidateBaseUri();
        if (validation.IsFail)
        {
            return validation.Match<Fin<LocalInferenceStatus>>(
                Succ: _ => throw new InvalidOperationException("Unexpected validation success."),
                Fail: error => Fin<LocalInferenceStatus>.Succ(new LocalInferenceStatus(
                    provider: "ollama",
                    enabled: _options.Enabled,
                    baseUrl: _baseUri.ToString(),
                    defaultModel: _options.DefaultModel,
                    availableModels: Array.Empty<string>(),
                    serverReachable: false,
                    message: error.Message)));
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(Math.Min(_options.TimeoutSeconds, 15)));

            var httpClient = _httpClientFactory.CreateClient("ollama");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, "api/tags"));
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("McpServer", "0.1.0"));

            using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedCts.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Fin<LocalInferenceStatus>.Succ(new LocalInferenceStatus(
                    provider: "ollama",
                    enabled: _options.Enabled,
                    baseUrl: _baseUri.ToString(),
                    defaultModel: _options.DefaultModel,
                    availableModels: Array.Empty<string>(),
                    serverReachable: false,
                    message: $"Ollama returned HTTP {(int)response.StatusCode}: {TrimForError(body)}"));
            }

            var models = ExtractModelNames(body);
            return Fin<LocalInferenceStatus>.Succ(new LocalInferenceStatus(
                provider: "ollama",
                enabled: _options.Enabled,
                baseUrl: _baseUri.ToString(),
                defaultModel: _options.DefaultModel,
                availableModels: models,
                serverReachable: true,
                message: null));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fin<LocalInferenceStatus>.Succ(new LocalInferenceStatus(
                provider: "ollama",
                enabled: _options.Enabled,
                baseUrl: _baseUri.ToString(),
                defaultModel: _options.DefaultModel,
                availableModels: Array.Empty<string>(),
                serverReachable: false,
                message: "Ollama status request timed out."));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama status check failed");
            return Fin<LocalInferenceStatus>.Succ(new LocalInferenceStatus(
                provider: "ollama",
                enabled: _options.Enabled,
                baseUrl: _baseUri.ToString(),
                defaultModel: _options.DefaultModel,
                availableModels: Array.Empty<string>(),
                serverReachable: false,
                message: ex.Message));
        }
    }

    private HttpRequestMessage BuildChatRequest(
        string model,
        LocalInferenceRequest request,
        double temperature)
    {
        var messages = new List<object>(string.IsNullOrWhiteSpace(request.SystemPrompt) ? 1 : 2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new
            {
                role = "system",
                content = request.SystemPrompt
            });
        }

        messages.Add(new
        {
            role = "user",
            content = request.Prompt
        });

        var options = BuildOllamaOptions(temperature);
        var payload = new
        {
            model,
            stream = false,
            messages,
            options
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "api/chat"));
        httpRequest.Headers.UserAgent.Add(new ProductInfoHeaderValue("McpServer", "0.1.0"));
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return httpRequest;
    }

    private object BuildOllamaOptions(double temperature)
    {
        var numPredict = _options.NumPredict ?? _options.MaxOutputChars;

        return new
        {
            temperature,
            num_ctx = _options.ContextLength,
            num_predict = Math.Clamp(numPredict, 1, _options.MaxOutputChars)
        };
    }

    private Fin<Unit> ValidateBaseUri()
    {
        if (!_options.Enabled)
        {
            return Error.New("Ollama integration is disabled by local MCPServer configuration.");
        }

        if (_baseUri.Scheme is not "http" and not "https")
        {
            return Error.New("Ollama BaseUrl must use HTTP or HTTPS.");
        }

        if (!_options.AllowNonLoopbackBaseUrl && !_baseUri.IsLoopback)
        {
            return Error.New("Ollama BaseUrl must be loopback unless AllowNonLoopbackBaseUrl is explicitly enabled.");
        }

        return Unit.Default;
    }

    private Fin<Unit> ValidateRequest(LocalInferenceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Error.New("Prompt is required.");
        }

        var promptChars = request.Prompt.Length + (request.SystemPrompt?.Length ?? 0);
        if (promptChars > _options.MaxPromptChars)
        {
            return Error.New($"Prompt exceeded max allowed characters: {_options.MaxPromptChars}.");
        }

        if (request.TimeoutSeconds is not null &&
            (request.TimeoutSeconds.Value < 1 || request.TimeoutSeconds.Value > _options.MaxTimeoutSeconds))
        {
            return Error.New($"timeoutSeconds must be between 1 and {_options.MaxTimeoutSeconds}.");
        }

        if (request.MaxOutputChars is not null &&
            (request.MaxOutputChars.Value < 256 || request.MaxOutputChars.Value > _options.MaxOutputChars))
        {
            return Error.New($"maxOutputChars must be between 256 and {_options.MaxOutputChars}.");
        }

        return Unit.Default;
    }

    private int ClampTimeout(int? requestedTimeoutSeconds)
    {
        var timeout = requestedTimeoutSeconds ?? _options.TimeoutSeconds;
        return Math.Clamp(timeout, 1, _options.MaxTimeoutSeconds);
    }

    private int ClampMaxOutputChars(int? requestedMaxOutputChars)
    {
        var maxOutputChars = requestedMaxOutputChars ?? _options.MaxOutputChars;
        return Math.Clamp(maxOutputChars, 256, _options.MaxOutputChars);
    }

    private string ResolveModel(string? requestedModel, out string? error)
    {
        error = null;
        var model = string.IsNullOrWhiteSpace(requestedModel)
            ? _options.DefaultModel
            : requestedModel.Trim();

        if (_options.AllowedModels.Count == 0)
        {
            if (!string.Equals(model, _options.DefaultModel, StringComparison.OrdinalIgnoreCase))
            {
                error = "Requested model is not allowed. Configure Ollama:AllowedModels to permit model switching.";
            }

            return model;
        }

        var allowedModels = _options.AllowedModels;
        var modelAllowed = false;
        foreach (var allowedModel in allowedModels)
        {
            if (string.Equals(allowedModel, model, StringComparison.OrdinalIgnoreCase))
            {
                modelAllowed = true;
                break;
            }
        }

        if (!modelAllowed)
        {
            error = $"Requested model is not allowed: {model}.";
        }

        return model;
    }

    private static Uri CreateBaseUri(string baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://127.0.0.1:11434"
            : baseUrl.Trim();

        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private static string? ExtractAssistantContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (root.TryGetProperty("response", out var response) &&
            response.ValueKind == JsonValueKind.String)
        {
            return response.GetString();
        }

        if (root.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractModelNames(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("models", out var models) ||
            models.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(models.GetArrayLength());
        foreach (var model in models.EnumerateArray())
        {
            if (model.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(name.GetString()))
            {
                names.Add(name.GetString()!);
            }
        }

        return names.ToArray();
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

    private static Fin<T> PropagateFailure<T>(Fin<Unit> failure)
    {
        return failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected validation failure while propagating result."),
            Fail: error => error);
    }
}
