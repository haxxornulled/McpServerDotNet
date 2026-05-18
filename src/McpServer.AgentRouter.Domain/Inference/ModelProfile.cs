namespace McpServer.AgentRouter.Domain.Inference;

public sealed class ModelProfile
{
    public ModelProfile(
        string name,
        string provider,
        string model,
        Uri baseUri,
        int contextLength,
        int maxOutputTokens,
        double temperature,
        bool allowCloudProvider,
        bool allowNonLoopbackBaseUrl,
        int timeoutSeconds)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Profile name is required.", nameof(name))
            : name.Trim();

        Provider = string.IsNullOrWhiteSpace(provider)
            ? throw new ArgumentException("Provider is required.", nameof(provider))
            : provider.Trim();

        Model = string.IsNullOrWhiteSpace(model)
            ? throw new ArgumentException("Model is required.", nameof(model))
            : model.Trim();

        BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
        ContextLength = Math.Clamp(contextLength, 2048, 1_048_576);
        MaxOutputTokens = Math.Clamp(maxOutputTokens, 1, 131072);
        Temperature = Math.Clamp(temperature, 0.0d, 2.0d);
        AllowCloudProvider = allowCloudProvider;
        AllowNonLoopbackBaseUrl = allowNonLoopbackBaseUrl;
        TimeoutSeconds = Math.Clamp(timeoutSeconds, 1, 3600);
    }

    public string Name { get; }

    public string Provider { get; }

    public string Model { get; }

    public Uri BaseUri { get; }

    public int ContextLength { get; }

    public int MaxOutputTokens { get; }

    public double Temperature { get; }

    public bool AllowCloudProvider { get; }

    public bool AllowNonLoopbackBaseUrl { get; }

    public int TimeoutSeconds { get; }
}
