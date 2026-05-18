namespace McpServer.Application.Inference;

public sealed class LocalInferenceStatus
{
    public LocalInferenceStatus(
        string provider,
        bool enabled,
        string baseUrl,
        string defaultModel,
        IReadOnlyList<string> availableModels,
        bool serverReachable,
        string? message)
    {
        Provider = provider;
        Enabled = enabled;
        BaseUrl = baseUrl;
        DefaultModel = defaultModel;
        AvailableModels = availableModels;
        ServerReachable = serverReachable;
        Message = message;
    }

    public string Provider { get; }

    public bool Enabled { get; }

    public string BaseUrl { get; }

    public string DefaultModel { get; }

    public IReadOnlyList<string> AvailableModels { get; }

    public bool ServerReachable { get; }

    public string? Message { get; }
}
