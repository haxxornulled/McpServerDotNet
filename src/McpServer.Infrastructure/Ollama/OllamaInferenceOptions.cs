namespace McpServer.Infrastructure.Ollama;

public sealed class OllamaInferenceOptions
{
    public OllamaInferenceOptions(
        bool enabled,
        string baseUrl,
        string defaultModel,
        IReadOnlyCollection<string>? allowedModels,
        int timeoutSeconds,
        int maxTimeoutSeconds,
        int maxPromptChars,
        int maxOutputChars,
        int contextLength,
        int? numPredict,
        double temperature,
        bool allowNonLoopbackBaseUrl)
    {
        Enabled = enabled;
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl.Trim();
        DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "qwen25-coder-14b-64k" : defaultModel.Trim();
        AllowedModels = NormalizeAllowedModels(allowedModels);
        TimeoutSeconds = Math.Max(1, timeoutSeconds);
        MaxTimeoutSeconds = Math.Max(TimeoutSeconds, maxTimeoutSeconds);
        MaxPromptChars = Math.Max(1024, maxPromptChars);
        MaxOutputChars = Math.Max(512, maxOutputChars);
        ContextLength = Math.Clamp(contextLength, 2048, 1_048_576);
        NumPredict = numPredict is null ? null : Math.Clamp(numPredict.Value, 1, MaxOutputChars);
        Temperature = Math.Clamp(temperature, 0.0d, 2.0d);
        AllowNonLoopbackBaseUrl = allowNonLoopbackBaseUrl;
    }

    public bool Enabled { get; }

    public string BaseUrl { get; }

    public string DefaultModel { get; }

    public IReadOnlyCollection<string> AllowedModels { get; }

    public int TimeoutSeconds { get; }

    public int MaxTimeoutSeconds { get; }

    public int MaxPromptChars { get; }

    public int MaxOutputChars { get; }

    public int ContextLength { get; }

    public int? NumPredict { get; }

    public double Temperature { get; }

    public bool AllowNonLoopbackBaseUrl { get; }

    private static IReadOnlyCollection<string> NormalizeAllowedModels(IReadOnlyCollection<string>? allowedModels)
    {
        if (allowedModels is null || allowedModels.Count == 0)
        {
            return global::System.Array.AsReadOnly(global::System.Array.Empty<string>());
        }

        var values = allowedModels
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return global::System.Array.AsReadOnly(values);
    }
}
