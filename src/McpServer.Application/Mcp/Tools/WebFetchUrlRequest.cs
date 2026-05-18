using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record WebFetchUrlRequest(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 30);
}
