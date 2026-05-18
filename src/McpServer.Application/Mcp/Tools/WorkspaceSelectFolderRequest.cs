using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceSelectFolderRequest(
    [property: JsonPropertyName("path")] string? Path = null);
