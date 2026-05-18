using System.Text.Json;
using System.Text.Json.Serialization;
using McpServer.Protocol.Shared;

namespace McpServer.Protocol.Tools;

public sealed record ToolDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("inputSchema")] JsonElement InputSchema);

public sealed record ListToolsRequestParams(
    [property: JsonPropertyName("cursor")] string? Cursor = null);

public sealed record ListToolsResult(
    [property: JsonPropertyName("tools")] IReadOnlyList<ToolDto> Tools,
    [property: JsonPropertyName("nextCursor")] string? NextCursor = null);

public sealed record CallToolRequestParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement Arguments);

public sealed record CallToolResultDto(
    [property: JsonPropertyName("content")] IReadOnlyList<TextContentDto> Content,
    [property: JsonPropertyName("structuredContent")] object? StructuredContent = null,
    [property: JsonPropertyName("isError")] bool? IsError = null);

public sealed record ToolErrorDto(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("message")] string Message);
