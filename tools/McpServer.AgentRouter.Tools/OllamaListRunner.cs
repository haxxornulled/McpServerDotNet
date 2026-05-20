using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class OllamaListSettings
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";

    public int TimeoutSeconds { get; init; } = 15;

    public static OllamaListSettings FromOptions(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new OllamaListSettings
        {
            BaseUrl = options.GetString("base-url", options.GetString("ollama-base-url", "http://127.0.0.1:11434")),
            TimeoutSeconds = options.GetInt("timeout-seconds", 15)
        };
    }
}

internal sealed class OllamaListRunner
{
    private readonly OllamaListSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly System.IO.TextWriter _output;
    private readonly System.IO.TextWriter _error;

    public OllamaListRunner(
        OllamaListSettings settings,
        HttpClient httpClient,
        System.IO.TextWriter output,
        System.IO.TextWriter error)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public async Task<OllamaListResult> RunAsync(CancellationToken cancellationToken)
    {
        var baseUrl = NormalizeBaseUrl(_settings.BaseUrl);
        if (!CliOutput.IsJson)
        {
            ConsoleWriter.WriteSection("Ollama model catalog");
        }

        var response = await HttpJson.GetAsync(_httpClient, new Uri(baseUrl, "api/tags"), cancellationToken).ConfigureAwait(false);
        if (!response.Success || response.Json is null)
        {
            if (!CliOutput.IsJson)
            {
                _error.WriteLine(response.ErrorMessage);
            }

            return new OllamaListResult(baseUrl.ToString(), false, response.ErrorMessage, Array.Empty<OllamaModelSummary>());
        }

        var models = ParseModels(response.Json);
        if (!CliOutput.IsJson)
        {
            WriteHumanReadable(baseUrl, models);
        }

        return new OllamaListResult(baseUrl.ToString(), true, null, models);
    }

    private void WriteHumanReadable(Uri baseUrl, IReadOnlyList<OllamaModelSummary> models)
    {
        _output.WriteLine(FormattableString.Invariant($"Base URL : {baseUrl}"));
        _output.WriteLine(FormattableString.Invariant($"Models   : {models.Count}"));

        if (models.Count == 0)
        {
            _output.WriteLine("No models were returned.");
            return;
        }

        _output.WriteLine();
        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            _output.WriteLine(FormattableString.Invariant($"{index + 1}. {model.Name}"));

            if (!string.IsNullOrWhiteSpace(model.Size))
            {
                _output.WriteLine(FormattableString.Invariant($"   size: {model.Size}"));
            }

            if (!string.IsNullOrWhiteSpace(model.ModifiedAt))
            {
                _output.WriteLine(FormattableString.Invariant($"   modified: {model.ModifiedAt}"));
            }

            if (!string.IsNullOrWhiteSpace(model.Digest))
            {
                _output.WriteLine(FormattableString.Invariant($"   digest: {model.Digest}"));
            }
        }
    }

    internal static Uri NormalizeBaseUrl(string baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl) ? "http://127.0.0.1:11434" : baseUrl.Trim();
        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    internal static IReadOnlyList<OllamaModelSummary> ParseModels(JsonNode json)
    {
        if (json["models"] is not JsonArray modelsArray || modelsArray.Count == 0)
        {
            return Array.Empty<OllamaModelSummary>();
        }

        var models = new List<OllamaModelSummary>(modelsArray.Count);
        foreach (var item in modelsArray)
        {
            if (item is not JsonObject modelObject)
            {
                continue;
            }

            var name = modelObject["name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            models.Add(new OllamaModelSummary(
                name,
                modelObject["size"]?.ToString(),
                modelObject["modified_at"]?.GetValue<string>(),
                modelObject["digest"]?.GetValue<string>(),
                modelObject["details"]?["family"]?.GetValue<string>(),
                modelObject["details"]?["quantization_level"]?.GetValue<string>()));
        }

        return models;
    }
}

internal sealed record OllamaListResult(
    string BaseUrl,
    bool ServerReachable,
    string? Message,
    IReadOnlyList<OllamaModelSummary> Models);

internal sealed record OllamaModelSummary(
    string Name,
    string? Size,
    string? ModifiedAt,
    string? Digest,
    string? Family,
    string? QuantizationLevel);
