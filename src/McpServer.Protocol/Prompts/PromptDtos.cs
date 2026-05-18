using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpServer.Protocol.Prompts;

public sealed record PromptArgumentDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("required")] bool Required);

public sealed record PromptDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("arguments")] IReadOnlyList<PromptArgumentDto>? Arguments);

public sealed record ListPromptsRequestParams(
    [property: JsonPropertyName("cursor")] string? Cursor = null);

public sealed record ListPromptsResult(
    [property: JsonPropertyName("prompts")] IReadOnlyList<PromptDto> Prompts,
    [property: JsonPropertyName("nextCursor")] string? NextCursor = null);

public sealed record GetPromptRequestParams(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] JsonElement? Arguments = null);

public sealed record PromptMessageContentDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text)
{
    public static PromptMessageContentDto FromText(string text) => new("text", text);
}

public sealed record PromptMessageDto(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] PromptMessageContentDto Content);

public sealed record GetPromptResultDto(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("messages")] IReadOnlyList<PromptMessageDto> Messages);
