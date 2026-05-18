using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Inference;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

internal static class LocalInferenceToolSupport
{
    public static JsonElement CreateTextGenerationSchema(string requiredProperty, object properties)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties,
            required = new[] { requiredProperty }
        });
    }

    public static void ValidateOptionalGenerationControls(
        List<string> errors,
        double? temperature,
        int? maxOutputChars,
        int? timeoutSeconds)
    {
        if (temperature is not null && (temperature.Value < 0.0d || temperature.Value > 2.0d))
        {
            errors.Add("Argument 'temperature' must be between 0 and 2.");
        }

        if (maxOutputChars is not null)
        {
            ToolRequestValidation.RequireRange(errors, maxOutputChars.Value, "maxOutputChars", 256, 200000);
        }

        if (timeoutSeconds is not null)
        {
            ToolRequestValidation.RequireRange(errors, timeoutSeconds.Value, "timeoutSeconds", 1, 600);
        }
    }

    public static async ValueTask<Fin<CallToolResult>> CompleteAsync(
        string toolName,
        ILocalInferenceService inferenceService,
        ILogger logger,
        LocalInferenceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await inferenceService.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Map(response =>
        {
            logger.LogInformation(
                "Tool {ToolName} completed local inference operation {Operation} using {Provider}/{Model}",
                toolName,
                response.Operation,
                response.Provider,
                response.Model);

            var text = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult(
                new[] { new ContentItem("text", text) },
                StructuredContent: response);
        });
    }
}
