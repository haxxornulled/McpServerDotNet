using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record FsListDirectoryRequest(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("search_pattern")] string? SearchPattern = null);
}
