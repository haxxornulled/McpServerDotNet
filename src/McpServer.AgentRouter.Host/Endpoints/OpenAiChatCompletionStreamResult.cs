using System.Text;
using System.Text.Json;
using McpServer.AgentRouter.Domain.Inference;
using McpServer.AgentRouter.Host.Protocol.OpenAi;
using Microsoft.AspNetCore.Http;

namespace McpServer.AgentRouter.Host.Endpoints;

internal sealed class OpenAiChatCompletionStreamResult : IResult
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _model;
    private readonly IAsyncEnumerable<ModelTurnChunk> _chunks;
    private readonly string _id = "chatcmpl-" + Guid.NewGuid().ToString("N");
    private readonly long _created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public OpenAiChatCompletionStreamResult(
        string model,
        IAsyncEnumerable<ModelTurnChunk> chunks)
    {
        _model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model is required.", nameof(model))
            : model.Trim();
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        var responseStarted = false;
        try
        {
            await using var enumerator = _chunks.GetAsyncEnumerator(httpContext.RequestAborted);

            while (true)
            {
                var hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                if (!hasNext)
                {
                    break;
                }

                var chunk = enumerator.Current;

                if (chunk.IsFinal)
                {
                    await EnsureStreamingResponseStartedAsync(response, httpContext.RequestAborted).ConfigureAwait(false);
                    responseStarted = true;

                    await WriteDataEventAsync(
                            response,
                            BuildChunkResponse(finishReason: chunk.FinishReason ?? "stop", content: null, includeRole: false),
                            httpContext.RequestAborted)
                        .ConfigureAwait(false);

                    await WriteDoneAsync(response, httpContext.RequestAborted).ConfigureAwait(false);
                    return;
                }

                if (string.IsNullOrWhiteSpace(chunk.Content))
                {
                    continue;
                }

                await EnsureStreamingResponseStartedAsync(response, httpContext.RequestAborted).ConfigureAwait(false);
                responseStarted = true;

                await WriteDataEventAsync(
                        response,
                        BuildChunkResponse(
                            finishReason: null,
                            content: chunk.Content,
                            includeRole: true),
                        httpContext.RequestAborted)
                    .ConfigureAwait(false);
            }

            await EnsureStreamingResponseStartedAsync(response, httpContext.RequestAborted).ConfigureAwait(false);
            responseStarted = true;

            await WriteDataEventAsync(
                    response,
                    BuildChunkResponse(finishReason: "stop", content: null, includeRole: false),
                    httpContext.RequestAborted)
                .ConfigureAwait(false);

            await WriteDoneAsync(response, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; stop writing.
        }
        catch (Exception ex)
        {
            if (!responseStarted && !response.HasStarted)
            {
                await WriteHttpErrorResponseAsync(response, ex.Message, httpContext.RequestAborted).ConfigureAwait(false);
                return;
            }

            await WriteErrorEventAsync(response, ex.Message, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }

    private static async Task EnsureStreamingResponseStartedAsync(
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (response.HasStarted)
        {
            return;
        }

        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Pragma = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private OpenAiChatCompletionChunk BuildChunkResponse(
        string? finishReason,
        string? content,
        bool includeRole)
    {
        var chunk = new OpenAiChatCompletionChunk
        {
            Id = _id,
            Created = _created,
            Model = _model
        };

        chunk.Choices.Add(new OpenAiChatCompletionChunkChoice
        {
            Index = 0,
            Delta = new OpenAiChatCompletionDelta
            {
                Role = includeRole ? "assistant" : null,
                Content = content
            },
            FinishReason = finishReason
        });

        return chunk;
    }

    private static async Task WriteDataEventAsync<T>(
        HttpResponse response,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await response.WriteAsync("data: ", cancellationToken).ConfigureAwait(false);
        await response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await response.WriteAsync("\n\n", cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteDoneAsync(
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        await response.WriteAsync("data: [DONE]\n\n", cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteErrorEventAsync(
        HttpResponse response,
        string message,
        CancellationToken cancellationToken)
    {
        var error = new OpenAiErrorResponse
        {
            Error = new OpenAiError
            {
                Message = string.IsNullOrWhiteSpace(message) ? "Streaming chat completion failed." : message,
                Type = "stream_error",
                Code = "stream_error"
            }
        };

        await response.WriteAsync("event: error\n", cancellationToken).ConfigureAwait(false);
        await response.WriteAsync("data: ", cancellationToken).ConfigureAwait(false);
        await response.WriteAsync(JsonSerializer.Serialize(error, JsonOptions), cancellationToken).ConfigureAwait(false);
        await response.WriteAsync("\n\n", cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteHttpErrorResponseAsync(
        HttpResponse response,
        string message,
        CancellationToken cancellationToken)
    {
        var error = new OpenAiErrorResponse
        {
            Error = new OpenAiError
            {
                Message = string.IsNullOrWhiteSpace(message) ? "Streaming chat completion failed." : message,
                Type = "stream_error",
                Code = "stream_error"
            }
        };

        response.StatusCode = StatusCodes.Status502BadGateway;
        response.ContentType = "application/json; charset=utf-8";

        var json = JsonSerializer.Serialize(error, JsonOptions);
        await response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
