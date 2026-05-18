using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityRoutingResult(
    [property: JsonPropertyName("activity")] ActivityKind Activity,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("requiresWorkspace")] bool RequiresWorkspace,
    [property: JsonPropertyName("requiresShell")] bool RequiresShell,
    [property: JsonPropertyName("requiresStructuredOutput")] bool RequiresStructuredOutput,
    [property: JsonPropertyName("schemaName")] string SchemaName);
