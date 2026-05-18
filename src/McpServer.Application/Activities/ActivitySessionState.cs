using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivitySessionState(
    [property: JsonPropertyName("lastActivity")] ActivityKind? LastActivity,
    [property: JsonPropertyName("lastUserRequest")] string? LastUserRequest,
    [property: JsonPropertyName("lastSchemaName")] string? LastSchemaName,
    [property: JsonPropertyName("lastSummary")] string? LastSummary,
    [property: JsonPropertyName("lastBuildPassed")] bool LastBuildPassed,
    [property: JsonPropertyName("lastUnitTestsPassed")] bool LastUnitTestsPassed,
    [property: JsonPropertyName("lastIntegrationTestsPassed")] bool LastIntegrationTestsPassed,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt)
{
    public static ActivitySessionState Empty { get; } = new(null, null, null, null, false, false, false, DateTimeOffset.MinValue);
}
