using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityContextPreviewRequest(
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("activity")] string? Activity = null,
    [property: JsonPropertyName("maxContextBytes")] int MaxContextBytes = 0);
