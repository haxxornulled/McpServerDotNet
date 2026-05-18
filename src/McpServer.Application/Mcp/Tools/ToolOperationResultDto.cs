using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed record ToolOperationResultDto(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("sourcePath")] string? SourcePath = null,
    [property: JsonPropertyName("destinationPath")] string? DestinationPath = null);
