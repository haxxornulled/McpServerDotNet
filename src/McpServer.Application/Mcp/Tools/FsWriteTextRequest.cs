using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record FsWriteTextRequest(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("encoding")] string? Encoding = null,
        [property: JsonPropertyName("overwrite")] bool Overwrite = true);
}
