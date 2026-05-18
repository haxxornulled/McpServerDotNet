using System.Text.Json.Serialization;
namespace McpServer.Application.Activities;

public sealed record ActivitySchemasListResult(
    [property: JsonPropertyName("schemas")] IReadOnlyList<StructuredOutputSchemaDto> Schemas);
