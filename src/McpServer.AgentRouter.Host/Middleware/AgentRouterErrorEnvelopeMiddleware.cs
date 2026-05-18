using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using McpServer.AgentRouter.Host.Protocol.OpenAi;

namespace McpServer.AgentRouter.Host.Middleware;

public static class AgentRouterErrorEnvelopeMiddleware
{
    public static IApplicationBuilder UseAgentRouterErrorEnvelope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (BadHttpRequestException exception) when (IsMalformedJsonRequest(exception))
            {
                await WriteInvalidJsonAsync(context, exception).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                await WriteInvalidJsonAsync(context, exception).ConfigureAwait(false);
            }
        });
    }

    private static bool IsMalformedJsonRequest(BadHttpRequestException exception)
    {
        return exception.StatusCode == StatusCodes.Status400BadRequest
            && (exception.InnerException is JsonException
                || exception.Message.Contains("Failed to read parameter", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("JSON", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteInvalidJsonAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            throw exception;
        }

        var loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("McpServer.AgentRouter.Host.Middleware.AgentRouterErrorEnvelopeMiddleware");
        logger?.LogWarning(exception, "Rejected malformed JSON request body for {Method} {Path}.", context.Request.Method, context.Request.Path);

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";

        var response = new OpenAiErrorResponse
        {
            Error = new OpenAiError
            {
                Message = "Invalid JSON request body.",
                Type = "invalid_request_error",
                Param = "body",
                Code = "invalid_json"
            }
        };

        await context.Response.WriteAsJsonAsync(response).ConfigureAwait(false);
    }
}
