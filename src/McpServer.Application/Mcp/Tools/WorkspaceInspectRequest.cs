using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceInspectRequest(
    [property: JsonPropertyName("path")] string Path = "workspace",
    [property: JsonPropertyName("maxDepth")] int MaxDepth = 4,
    [property: JsonPropertyName("maxFiles")] int MaxFiles = 24,
    [property: JsonPropertyName("maxFileBytes")] int MaxFileBytes = 12000,
    [property: JsonPropertyName("maxTotalFileBytes")] int MaxTotalFileBytes = 32768);
