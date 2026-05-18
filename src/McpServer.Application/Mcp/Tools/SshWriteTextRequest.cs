using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record SshWriteTextRequest(
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string Content);
}
