using System.Text.Json.Serialization;

namespace McpServer.Protocol.Resources;

public sealed record ResourceDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("mimeType")] string? MimeType = null,
    [property: JsonPropertyName("size")] long? Size = null);

public sealed record ListResourcesRequestParams(
    [property: JsonPropertyName("cursor")] string? Cursor = null);

public sealed record ListResourcesResult(
    [property: JsonPropertyName("resources")] IReadOnlyList<ResourceDto> Resources,
    [property: JsonPropertyName("nextCursor")] string? NextCursor = null);

public sealed record ReadResourceRequestParams(
    [property: JsonPropertyName("uri")] string Uri);

public sealed record ReadResourceResultDto(
    [property: JsonPropertyName("contents")] IReadOnlyList<object> Contents);
