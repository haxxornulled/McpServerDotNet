using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Activities;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class ActivityRouteToolHandler(
    IActivityRouter activityRouter,
    ILogger<ActivityRouteToolHandler> logger) : IToolHandler<ActivityRouteRequest>
{
    public string Name => "activity.route";
    public string Description => "Classifies a user request into the dynamic MCP activity that should handle it, including the structured-output schema to use.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                request = new
                {
                    type = "string",
                    minLength = 1,
                    description = "The user request to classify into an activity."
                }
            },
            required = new[] { "request" }
        });

    public IReadOnlyList<string> Validate(ActivityRouteRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Request, "request");
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(ActivityRouteRequest request, CancellationToken ct)
    {
        var result = await activityRouter.RouteAsync(request.Request, ct).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation(
            "Tool {ToolName} routed request to activity {Activity} schema {SchemaName} confidence {Confidence}",
            Name,
            result.Activity,
            result.SchemaName,
            result.Confidence);

        return new CallToolResult([new ContentItem("text", json)], StructuredContent: result);
    }
}
