using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record CreateDirectoryRequest(
    [property: JsonPropertyName("path")] string Path);
