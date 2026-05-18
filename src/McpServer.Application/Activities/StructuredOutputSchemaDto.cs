using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record StructuredOutputSchemaDto(
    [property: JsonPropertyName("schemaName")] string SchemaName,
    [property: JsonPropertyName("activity")] ActivityKind Activity,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("strict")] bool Strict,
    [property: JsonPropertyName("responseFormat")] JsonElement ResponseFormat);
