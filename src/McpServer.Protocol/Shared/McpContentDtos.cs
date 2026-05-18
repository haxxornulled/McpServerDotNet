using System.Text.Json.Serialization;

namespace McpServer.Protocol.Shared;

public sealed record TextContentDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text)
{
    public static TextContentDto Create(string text) => new("text", text);
}

public sealed record TextResourceContentsDto(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("mimeType")] string? MimeType,
    [property: JsonPropertyName("text")] string Text);

public sealed record BlobResourceContentsDto(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("mimeType")] string? MimeType,
    [property: JsonPropertyName("blob")] string BlobBase64);
