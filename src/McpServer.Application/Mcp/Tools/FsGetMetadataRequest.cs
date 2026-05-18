using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record FsGetMetadataRequest(
        [property: JsonPropertyName("path")] string Path);
}
