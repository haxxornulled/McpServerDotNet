using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceInspectEntryDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory);

public sealed record WorkspaceInspectFileDto(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("lineNumberedContent")] string LineNumberedContent,
    [property: JsonPropertyName("truncated")] bool Truncated);

public sealed record WorkspaceInspectResult(
    [property: JsonPropertyName("root")] string Root,
    [property: JsonPropertyName("entries")] IReadOnlyList<WorkspaceInspectEntryDto> Entries,
    [property: JsonPropertyName("files")] IReadOnlyList<WorkspaceInspectFileDto> Files,
    [property: JsonPropertyName("truncated")] bool Truncated);
