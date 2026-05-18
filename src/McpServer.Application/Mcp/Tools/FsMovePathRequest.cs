using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record FsMovePathRequest(
        [property: JsonPropertyName("source_path")] string SourcePath,
        [property: JsonPropertyName("destination_path")] string DestinationPath,
        [property: JsonPropertyName("overwrite")] bool Overwrite = false);
}
