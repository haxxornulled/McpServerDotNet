using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Prompts;

public sealed record SummarizeFilePromptArguments(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("focus")] string? Focus = null);
