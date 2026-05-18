using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceOpenRequest(
    [property: JsonPropertyName("path")] string Path);
