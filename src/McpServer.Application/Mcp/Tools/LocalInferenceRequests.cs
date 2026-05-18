using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools;

public sealed class LocalCompleteRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("maxOutputChars")]
    public int? MaxOutputChars { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }
}

public sealed class LocalSummarizeRequest
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("focus")]
    public string? Focus { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("maxOutputChars")]
    public int? MaxOutputChars { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }
}

public sealed class LocalCodeReviewRequest
{
    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string? FilePath { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("maxOutputChars")]
    public int? MaxOutputChars { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }
}

public sealed class LocalPlanRequest
{
    [JsonPropertyName("goal")]
    public string Goal { get; init; } = string.Empty;

    [JsonPropertyName("context")]
    public string? Context { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("maxOutputChars")]
    public int? MaxOutputChars { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }
}

public sealed class LocalInferenceStatusRequest
{
}
