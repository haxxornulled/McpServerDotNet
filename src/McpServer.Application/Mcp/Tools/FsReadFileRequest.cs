using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record FsReadFileRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("encoding")] string? Encoding = null);
