namespace McpServer.AgentRouter.Domain.Inference;

public sealed class ChatTurnMessage
{
    public ChatTurnMessage(string role, string content)
    {
        Role = string.IsNullOrWhiteSpace(role)
            ? throw new ArgumentException("Role is required.", nameof(role))
            : role.Trim();

        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public string Role { get; }

    public string Content { get; }
}

public sealed class ModelInvocationRequest
{
    public ModelInvocationRequest(
        string modelProfileName,
        IReadOnlyList<ChatTurnMessage> messages,
        double? temperature,
        int? maxOutputTokens)
    {
        ModelProfileName = string.IsNullOrWhiteSpace(modelProfileName)
            ? throw new ArgumentException("Model profile name is required.", nameof(modelProfileName))
            : modelProfileName.Trim();

        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        Temperature = temperature;
        MaxOutputTokens = maxOutputTokens;
    }

    public string ModelProfileName { get; }

    public IReadOnlyList<ChatTurnMessage> Messages { get; }

    public double? Temperature { get; }

    public int? MaxOutputTokens { get; }
}

public sealed class ModelTurnResult
{
    public ModelTurnResult(
        string provider,
        string model,
        string content,
        string finishReason,
        int promptTokens,
        int completionTokens,
        long elapsedMilliseconds)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        FinishReason = string.IsNullOrWhiteSpace(finishReason) ? "stop" : finishReason.Trim();
        PromptTokens = Math.Max(0, promptTokens);
        CompletionTokens = Math.Max(0, completionTokens);
        ElapsedMilliseconds = Math.Max(0L, elapsedMilliseconds);
    }

    public string Provider { get; }

    public string Model { get; }

    public string Content { get; }

    public string FinishReason { get; }

    public int PromptTokens { get; }

    public int CompletionTokens { get; }

    public long ElapsedMilliseconds { get; }
}

public sealed class ModelTurnStream
{
    public ModelTurnStream(IAsyncEnumerable<ModelTurnChunk> chunks)
    {
        Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    public IAsyncEnumerable<ModelTurnChunk> Chunks { get; }
}

public sealed class ModelTurnChunk
{
    public ModelTurnChunk(
        string content,
        bool isFinal,
        string? finishReason = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        IsFinal = isFinal;
        FinishReason = string.IsNullOrWhiteSpace(finishReason) ? null : finishReason.Trim();
    }

    public string Content { get; }

    public bool IsFinal { get; }

    public string? FinishReason { get; }
}
