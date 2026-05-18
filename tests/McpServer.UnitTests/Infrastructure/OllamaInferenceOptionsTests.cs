using McpServer.Infrastructure.Ollama;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class OllamaInferenceOptionsTests
{
    [Fact]
    public void Constructor_Should_Default_BaseUrl_When_Blank()
    {
        var options = CreateOptions(baseUrl: "   ");

        Assert.Equal("http://127.0.0.1:11434", options.BaseUrl);
    }

    [Fact]
    public void Constructor_Should_Default_Model_When_Blank()
    {
        var options = CreateOptions(defaultModel: "   ");

        Assert.Equal("qwen25-coder-14b-64k", options.DefaultModel);
    }

    [Fact]
    public void Constructor_Should_Normalize_AllowedModels()
    {
        var options = CreateOptions(allowedModels: [" qwen3-coder:30b ", "QWEN3-CODER:30B", "devstral-small-2", ""]);

        Assert.Equal(2, options.AllowedModels.Count);
        Assert.Contains("qwen3-coder:30b", options.AllowedModels);
        Assert.Contains("devstral-small-2", options.AllowedModels);
    }

    [Fact]
    public void Constructor_Should_Clamp_ContextLength_To_Minimum()
    {
        var options = CreateOptions(contextLength: 1);

        Assert.Equal(2048, options.ContextLength);
    }

    [Fact]
    public void Constructor_Should_Clamp_ContextLength_To_Maximum()
    {
        var options = CreateOptions(contextLength: 2_000_000);

        Assert.Equal(1_048_576, options.ContextLength);
    }

    [Fact]
    public void Constructor_Should_Clamp_NumPredict_To_MaxOutputChars()
    {
        var options = CreateOptions(numPredict: 100_000, maxOutputChars: 32_000);

        Assert.Equal(32_000, options.NumPredict);
    }

    [Fact]
    public void Constructor_Should_Clamp_Temperature_To_Legal_Range()
    {
        var low = CreateOptions(temperature: -1.0d);
        var high = CreateOptions(temperature: 10.0d);

        Assert.Equal(0.0d, low.Temperature);
        Assert.Equal(2.0d, high.Temperature);
    }

    [Fact]
    public void Constructor_Should_Ensure_MaxTimeout_Is_At_Least_DefaultTimeout()
    {
        var options = CreateOptions(timeoutSeconds: 120, maxTimeoutSeconds: 10);

        Assert.Equal(120, options.MaxTimeoutSeconds);
    }

    private static OllamaInferenceOptions CreateOptions(
        bool enabled = true,
        string baseUrl = "http://127.0.0.1:11434",
        string defaultModel = "qwen3-coder:30b",
        IReadOnlyCollection<string>? allowedModels = null,
        int timeoutSeconds = 120,
        int maxTimeoutSeconds = 900,
        int maxPromptChars = 500_000,
        int maxOutputChars = 32_000,
        int contextLength = 131_072,
        int? numPredict = 32_000,
        double temperature = 0.15d,
        bool allowNonLoopbackBaseUrl = false)
    {
        return new OllamaInferenceOptions(
            enabled: enabled,
            baseUrl: baseUrl,
            defaultModel: defaultModel,
            allowedModels: allowedModels,
            timeoutSeconds: timeoutSeconds,
            maxTimeoutSeconds: maxTimeoutSeconds,
            maxPromptChars: maxPromptChars,
            maxOutputChars: maxOutputChars,
            contextLength: contextLength,
            numPredict: numPredict,
            temperature: temperature,
            allowNonLoopbackBaseUrl: allowNonLoopbackBaseUrl);
    }
}
