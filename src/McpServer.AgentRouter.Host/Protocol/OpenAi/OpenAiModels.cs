using System.Text.Json.Serialization;

namespace McpServer.AgentRouter.Host.Protocol.OpenAi;

public sealed class OpenAiErrorResponse
{
    [JsonPropertyName("error")]
    public OpenAiError Error { get; set; } = new OpenAiError();
}

public sealed class OpenAiError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "invalid_request_error";

    [JsonPropertyName("param")]
    public string? Param { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }
}

public sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public sealed class OpenAiChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("messages")]
    public IList<OpenAiChatMessage> Messages { get; set; } = new List<OpenAiChatMessage>();

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("stream")]
    public bool? Stream { get; set; }
}

public sealed class OpenAiChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public IList<OpenAiChatCompletionChoice> Choices { get; set; } = new List<OpenAiChatCompletionChoice>();

    [JsonPropertyName("usage")]
    public OpenAiUsage Usage { get; set; } = new OpenAiUsage();
}

public sealed class OpenAiChatCompletionChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAiChatMessage Message { get; set; } = new OpenAiChatMessage();

    [JsonPropertyName("finish_reason")]
    public string FinishReason { get; set; } = "stop";
}

public sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public sealed class OpenAiChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public IList<OpenAiChatCompletionChunkChoice> Choices { get; set; } = new List<OpenAiChatCompletionChunkChoice>();
}

public sealed class OpenAiChatCompletionChunkChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("delta")]
    public OpenAiChatCompletionDelta Delta { get; set; } = new OpenAiChatCompletionDelta();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class OpenAiChatCompletionDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public sealed class OpenAiModelListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "list";

    [JsonPropertyName("data")]
    public IList<OpenAiModelDescriptor> Data { get; set; } = new List<OpenAiModelDescriptor>();
}

public sealed class OpenAiModelDescriptor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "model";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; set; } = "local";
}
