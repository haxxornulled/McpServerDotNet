using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Prompts;

public sealed record ReviewDirectoryPromptArguments(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("goal")] string? Goal = null);
