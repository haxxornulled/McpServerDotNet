using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityRunRequest(
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("activity")] string? Activity = null,
    [property: JsonPropertyName("maxContextBytes")] int MaxContextBytes = 0,
    [property: JsonPropertyName("runBuild")] bool RunBuild = false,
    [property: JsonPropertyName("runTests")] bool RunTests = false);
