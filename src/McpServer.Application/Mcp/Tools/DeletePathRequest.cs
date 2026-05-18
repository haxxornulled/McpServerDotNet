using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record DeletePathRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("recursive")] bool Recursive = false,
    [property: JsonPropertyName("confirmation")] string? Confirmation = null);
