using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WebScrapeUrlRequest(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("selector")] string Selector,
    [property: JsonPropertyName("attribute")] string? Attribute = null,
    [property: JsonPropertyName("maxResults")] int MaxResults = 5,
    [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 30);
