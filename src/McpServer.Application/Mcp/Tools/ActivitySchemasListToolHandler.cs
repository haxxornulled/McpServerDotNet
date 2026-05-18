using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Activities;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class ActivitySchemasListToolHandler(
    IStructuredOutputSchemaRegistry schemaRegistry,
    ILogger<ActivitySchemasListToolHandler> logger) : IToolHandler<ActivitySchemasListRequest>
{
    public string Name => "activity.schemas.list";
    public string Description => "Lists available structured-output schemas for activity-aware LM Studio workflows.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new { }
        });

    public ValueTask<Fin<CallToolResult>> Handle(ActivitySchemasListRequest request, CancellationToken ct)
    {
        var result = new ActivitySchemasListResult(schemaRegistry.ListSchemas());
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        logger.LogInformation("Tool {ToolName} listed {SchemaCount} activity schemas", Name, result.Schemas.Count);
        return ValueTask.FromResult<Fin<CallToolResult>>(new CallToolResult([new ContentItem("text", json)], StructuredContent: result));
    }
}
