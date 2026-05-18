using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record WorkspaceSetRootRequest(
    [property: JsonPropertyName("path")] string Path);
