using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceFolderOption(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path);

public sealed record WorkspaceSelectFolderResult(
    [property: JsonPropertyName("workspaceRoot")] string WorkspaceRoot,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("browsedPath")] string BrowsedPath,
    [property: JsonPropertyName("projectRootChanged")] bool ProjectRootChanged,
    [property: JsonPropertyName("folders")] IReadOnlyList<WorkspaceFolderOption> Folders);
