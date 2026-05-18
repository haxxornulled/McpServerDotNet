using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Resources;

public sealed record WorkspaceTreeNodeDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory,
    [property: JsonPropertyName("children")] IReadOnlyList<WorkspaceTreeNodeDto>? Children = null);

public sealed record WorkspaceTreeSnapshotDto(
    [property: JsonPropertyName("scopeRoot")] string ScopeRoot,
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("root")] WorkspaceTreeNodeDto Root,
    [property: JsonPropertyName("nodeCount")] int NodeCount,
    [property: JsonPropertyName("directoryCount")] int DirectoryCount,
    [property: JsonPropertyName("fileCount")] int FileCount);
