using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record MovePathRequest(
    [property: JsonPropertyName("sourcePath")] string SourcePath,
    [property: JsonPropertyName("destinationPath")] string DestinationPath,
    [property: JsonPropertyName("overwrite")] bool Overwrite = false);
