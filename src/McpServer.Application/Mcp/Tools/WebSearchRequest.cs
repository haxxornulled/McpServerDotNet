using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WebSearchRequest(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("maxResults")] int MaxResults = 5);
