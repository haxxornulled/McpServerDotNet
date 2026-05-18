using System.Text.Json.Serialization;

namespace McpServer.Protocol.Roots;

public sealed record RootDto(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("name")] string? Name = null);

public sealed record ListRootsRequestParams();

public sealed record ListRootsResultDto(
    [property: JsonPropertyName("roots")] IReadOnlyList<RootDto> Roots);
