using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceOpenResult(
    [property: JsonPropertyName("workspaceRoot")] string WorkspaceRoot,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("changed")] bool Changed,
    [property: JsonPropertyName("allowedRoots")] IReadOnlyList<string> AllowedRoots);
