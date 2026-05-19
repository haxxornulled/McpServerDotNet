using System.Text.Json.Serialization;

namespace McpServer.Application.Web.Results;

public sealed record WebScrapeMatch(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("href")] string? Href,
    [property: JsonPropertyName("attribute_name")] string? AttributeName,
    [property: JsonPropertyName("attribute_value")] string? AttributeValue);

public sealed record WebScrapeResult(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("selector")] string Selector,
    [property: JsonPropertyName("match_count")] int MatchCount,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("fetch_time_ms")] long FetchTimeMs,
    [property: JsonPropertyName("matches")] IReadOnlyList<WebScrapeMatch> Matches);
