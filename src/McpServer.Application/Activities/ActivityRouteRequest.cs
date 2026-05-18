using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityRouteRequest(
    [property: JsonPropertyName("request")] string Request);
