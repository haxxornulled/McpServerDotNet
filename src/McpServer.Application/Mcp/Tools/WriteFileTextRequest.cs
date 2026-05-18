using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WriteFileTextRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("encoding")] string? Encoding,
    [property: JsonPropertyName("overwrite")] bool Overwrite = true,
    [property: JsonPropertyName("flush")] bool Flush = false);
