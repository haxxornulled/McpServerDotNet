namespace McpServer.AgentRouter.Domain.Inference;

/// <summary>
/// Describes a model profile and its safety limits.
/// </summary>
public sealed class ModelProfile
{
    /// <summary>
    /// Initializes a new model profile.
    /// </summary>
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

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets the model identifier.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the base URI used for requests.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    /// Gets the configured context length.
    /// </summary>
    public int ContextLength { get; }

    /// <summary>
    /// Gets the maximum output token budget.
    /// </summary>
    public int MaxOutputTokens { get; }

    /// <summary>
    /// Gets the default sampling temperature.
    /// </summary>
    public double Temperature { get; }

    /// <summary>
    /// Gets a value indicating whether cloud providers are allowed.
    /// </summary>
    public bool AllowCloudProvider { get; }

    /// <summary>
    /// Gets a value indicating whether non-loopback base URIs are allowed.
    /// </summary>
    public bool AllowNonLoopbackBaseUrl { get; }

    /// <summary>
    /// Gets the request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; }
}
