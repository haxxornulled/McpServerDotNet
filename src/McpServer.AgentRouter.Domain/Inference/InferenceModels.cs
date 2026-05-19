namespace McpServer.AgentRouter.Domain.Inference;

/// <summary>
/// Represents a single chat turn message.
/// </summary>
public sealed class ChatTurnMessage
{
    /// <summary>
    /// Initializes a new chat turn message.
    /// </summary>
    public ChatTurnMessage(string role, string content)
    {
        Role = string.IsNullOrWhiteSpace(role)
            ? throw new ArgumentException("Role is required.", nameof(role))
            : role.Trim();

        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Gets the role for the message.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Gets the message content.
    /// </summary>
    public string Content { get; }
}

/// <summary>
/// Describes a model invocation request.
/// </summary>
public sealed class ModelInvocationRequest
{
    /// <summary>
    /// Initializes a new model invocation request.
    /// </summary>
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

    /// <summary>
    /// Gets the target model profile name.
    /// </summary>
    public string ModelProfileName { get; }

    /// <summary>
    /// Gets the chat messages supplied to the model.
    /// </summary>
    public IReadOnlyList<ChatTurnMessage> Messages { get; }

    /// <summary>
    /// Gets the optional sampling temperature.
    /// </summary>
    public double? Temperature { get; }

    /// <summary>
    /// Gets the optional maximum output token budget.
    /// </summary>
    public int? MaxOutputTokens { get; }
}

/// <summary>
/// Represents the result of a single model turn.
/// </summary>
public sealed class ModelTurnResult
{
    /// <summary>
    /// Initializes a new model turn result.
    /// </summary>
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

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the model name.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the generated content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets the finish reason reported by the provider.
    /// </summary>
    public string FinishReason { get; }

    /// <summary>
    /// Gets the number of prompt tokens consumed.
    /// </summary>
    public int PromptTokens { get; }

    /// <summary>
    /// Gets the number of completion tokens consumed.
    /// </summary>
    public int CompletionTokens { get; }

    /// <summary>
    /// Gets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; }
}

/// <summary>
/// Represents a stream of model turn chunks.
/// </summary>
public sealed class ModelTurnStream
{
    /// <summary>
    /// Initializes a new model turn stream.
    /// </summary>
    public ModelTurnStream(IAsyncEnumerable<ModelTurnChunk> chunks)
    {
        Chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
    }

    /// <summary>
    /// Gets the asynchronous stream of chunks.
    /// </summary>
    public IAsyncEnumerable<ModelTurnChunk> Chunks { get; }
}

/// <summary>
/// Represents a single chunk from a streamed model turn.
/// </summary>
public sealed class ModelTurnChunk
{
    /// <summary>
    /// Initializes a new model turn chunk.
    /// </summary>
    public ModelTurnChunk(
        string content,
        bool isFinal,
        string? finishReason = null)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        IsFinal = isFinal;
        FinishReason = string.IsNullOrWhiteSpace(finishReason) ? null : finishReason.Trim();
    }

    /// <summary>
    /// Gets the chunk content.
    /// </summary>
    public string Content { get; }

    /// <summary>
    /// Gets a value indicating whether this is the final chunk.
    /// </summary>
    public bool IsFinal { get; }

    /// <summary>
    /// Gets the final finish reason, if present.
    /// </summary>
    public string? FinishReason { get; }
}
