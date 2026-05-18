using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceStatusResult(
    [property: JsonPropertyName("workspaceRoot")] string WorkspaceRoot,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("allowedRoots")] IReadOnlyList<string> AllowedRoots);
