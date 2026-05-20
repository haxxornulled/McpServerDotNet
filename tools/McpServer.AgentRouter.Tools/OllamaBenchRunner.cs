using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class OllamaBenchSettings
{
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";

    public IReadOnlyList<string>? Models { get; init; }

    public string PromptSet { get; init; } = "activity-routing";

    public double Temperature { get; init; } = 0.0d;

    public int TimeoutSeconds { get; init; } = 60;

    public int MaxOutputChars { get; init; } = 512;

    public string ReportDirectory { get; init; } = string.Empty;

    public static OllamaBenchSettings FromOptions(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new OllamaBenchSettings
        {
            BaseUrl = options.GetString("base-url", options.GetString("ollama-base-url", "http://127.0.0.1:11434")),
            Models = ParseModels(options.GetString("models", string.Empty)),
            PromptSet = options.GetString("prompt-set", "activity-routing"),
            Temperature = options.GetNullableDouble("temperature") ?? 0.0d,
            TimeoutSeconds = options.GetInt("timeout-seconds", 60),
            MaxOutputChars = options.GetInt("max-output-chars", 512),
            ReportDirectory = options.GetString("report-dir", string.Empty)
        };
    }

    private static IReadOnlyList<string>? ParseModels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var values = value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            return null;
        }

        var models = new List<string>(values.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var model = item.Trim();
            if (seen.Add(model))
            {
                models.Add(model);
            }
        }

        return models.Count == 0 ? null : models;
    }
}

internal sealed class OllamaBenchRunner
{
    private static readonly JsonBenchmarkCase[] DefaultCases =
    [
        new("lyrics", "Give me the lyrics for Closer to the Heart by Rush.", "Explain", "explain_result"),
        new("build", "The build failed with CS0103 and MSB1009 after the last change.", "BuildFix", "build_fix_result"),
        new("tests", "Two xUnit tests failed after the refactor.", "TestFailureAnalysis", "test_failure_analysis_result"),
        new("review", "Please do a deep code review of this pull request.", "DeepCodeReview", "deep_code_review_result"),
        new("security", "We need to harden the app against SSRF and secret leakage.", "SecurityReview", "security_review_result"),
        new("diagnostic", "The LM Studio bridge is hanging on POST /v1/chat/completions.", "Diagnostic", "diagnostic_result"),
        new("plan", "Please make a plan for the Blazor extension architecture.", "ImplementationPlan", "implementation_plan_result")
    ];

    private readonly OllamaBenchSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly System.IO.TextWriter _output;
    private readonly System.IO.TextWriter _error;

    public OllamaBenchRunner(
        OllamaBenchSettings settings,
        HttpClient httpClient,
        System.IO.TextWriter output,
        System.IO.TextWriter error)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _error = error ?? throw new ArgumentNullException(nameof(error));
    }

    public async Task<OllamaBenchResult> RunAsync(CancellationToken cancellationToken)
    {
        var baseUrl = OllamaListRunner.NormalizeBaseUrl(_settings.BaseUrl);
        if (!CliOutput.IsJson)
        {
            ConsoleWriter.WriteSection("Ollama benchmark");
        }

        var catalog = await HttpJson.GetAsync(_httpClient, new Uri(baseUrl, "api/tags"), cancellationToken).ConfigureAwait(false);
        if (!catalog.Success || catalog.Json is null)
        {
            await WriteReportAsync(baseUrl, false, catalog.ErrorMessage, DefaultCases, Array.Empty<OllamaModelBenchmarkResult>(), cancellationToken).ConfigureAwait(false);
            if (!CliOutput.IsJson)
            {
                _error.WriteLine(catalog.ErrorMessage);
            }

            return new OllamaBenchResult(baseUrl.ToString(), false, catalog.ErrorMessage, Array.Empty<OllamaModelBenchmarkResult>(), DefaultCases);
        }

        var allModels = OllamaListRunner.ParseModels(catalog.Json);
        var selectedModels = FilterModels(allModels, _settings.Models);
        if (selectedModels.Count == 0)
        {
            var message = _settings.Models is null || _settings.Models.Count == 0
                ? "Ollama returned no models."
                : "No Ollama models matched the requested filter.";

            if (!CliOutput.IsJson)
            {
                _error.WriteLine(message);
            }

            await WriteReportAsync(baseUrl, true, message, DefaultCases, Array.Empty<OllamaModelBenchmarkResult>(), cancellationToken).ConfigureAwait(false);
            return new OllamaBenchResult(baseUrl.ToString(), true, message, Array.Empty<OllamaModelBenchmarkResult>(), DefaultCases);
        }

        var cases = GetCases(_settings.PromptSet);
        var modelResults = new List<OllamaModelBenchmarkResult>(selectedModels.Count);

        foreach (var model in selectedModels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var caseResults = new List<OllamaBenchmarkCaseResult>(cases.Count);
            foreach (var benchmarkCase in cases)
            {
                caseResults.Add(await RunCaseAsync(baseUrl, model, benchmarkCase, cancellationToken).ConfigureAwait(false));
            }

            modelResults.Add(SummarizeModel(model, caseResults));
        }

        var ranked = modelResults
            .OrderByDescending(static model => model.AverageScore)
            .ThenByDescending(static model => model.Accuracy)
            .ThenByDescending(static model => model.AverageConfidence)
            .ThenBy(static model => model.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!CliOutput.IsJson)
        {
            WriteHumanReadable(baseUrl, cases, ranked);
        }

        await WriteReportAsync(baseUrl, true, null, cases, ranked, cancellationToken).ConfigureAwait(false);

        return new OllamaBenchResult(baseUrl.ToString(), true, null, ranked, cases);
    }

    private async Task WriteReportAsync(
        Uri baseUrl,
        bool serverReachable,
        string? message,
        IReadOnlyList<JsonBenchmarkCase> cases,
        IReadOnlyList<OllamaModelBenchmarkResult> models,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ReportDirectory))
        {
            return;
        }

        await OllamaBenchReportWriter.WriteAsync(_settings, baseUrl, serverReachable, message, cases, models, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OllamaBenchmarkCaseResult> RunCaseAsync(
        Uri baseUrl,
        string model,
        JsonBenchmarkCase benchmarkCase,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(benchmarkCase);
        var payload = new
        {
            model,
            stream = false,
            format = "json",
            messages = new[]
            {
                new { role = "system", content = CreateSystemPrompt() },
                new { role = "user", content = prompt }
            },
            options = new
            {
                temperature = Math.Clamp(_settings.Temperature, 0.0d, 2.0d),
                num_ctx = 4096,
                num_predict = Math.Max(128, _settings.MaxOutputChars)
            }
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await HttpJson.PostAsync(_httpClient, new Uri(baseUrl, "api/chat"), payload, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (!response.Success || response.Json is null)
        {
            return new OllamaBenchmarkCaseResult(
                benchmarkCase.Id,
                benchmarkCase.Prompt,
                benchmarkCase.ExpectedActivity,
                benchmarkCase.ExpectedSchemaName,
                PredictedActivity: "error",
                PredictedSchemaName: string.Empty,
                Confidence: 0,
                Correct: false,
                Score: 0,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                ErrorMessage: response.ErrorMessage);
        }

        var content = JsonFieldReader.GetString(response.Json, "message", "content") ?? string.Empty;
        var parsed = TryParseModelOutput(content);
        if (parsed is null)
        {
            return new OllamaBenchmarkCaseResult(
                benchmarkCase.Id,
                benchmarkCase.Prompt,
                benchmarkCase.ExpectedActivity,
                benchmarkCase.ExpectedSchemaName,
                PredictedActivity: "unparsed",
                PredictedSchemaName: string.Empty,
                Confidence: 0,
                Correct: false,
                Score: 0,
                ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
                ErrorMessage: string.IsNullOrWhiteSpace(content) ? "Model returned empty content." : "Model output was not valid JSON.");
        }

        var predictedActivity = JsonFieldReader.GetString(parsed, "activity") ?? string.Empty;
        var predictedSchemaName = JsonFieldReader.GetString(parsed, "schemaName") ?? string.Empty;
        var confidence = TryReadDouble(parsed["confidence"], out var parsedConfidence) ? parsedConfidence : 0d;
        var correct = string.Equals(predictedActivity, benchmarkCase.ExpectedActivity, StringComparison.OrdinalIgnoreCase);
        var score = correct ? confidence : 0d;

        return new OllamaBenchmarkCaseResult(
            benchmarkCase.Id,
            benchmarkCase.Prompt,
            benchmarkCase.ExpectedActivity,
            benchmarkCase.ExpectedSchemaName,
            PredictedActivity: string.IsNullOrWhiteSpace(predictedActivity) ? "unknown" : predictedActivity,
            PredictedSchemaName: predictedSchemaName,
            Confidence: confidence,
            Correct: correct,
            Score: score,
            ElapsedMilliseconds: stopwatch.ElapsedMilliseconds,
            ErrorMessage: null);
    }

    private static List<JsonBenchmarkCase> GetCases(string promptSet)
    {
        if (!string.Equals(promptSet, "activity-routing", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown prompt set '{promptSet}'.", nameof(promptSet));
        }

        return DefaultCases.ToList();
    }

    private static string CreateSystemPrompt()
    {
        return """
            You are classifying user requests into a code-assistant routing activity.
            Return only valid JSON with exactly these properties:
            activity, confidence, reason, requiresWorkspace, requiresShell, requiresStructuredOutput, schemaName.
            activity must be one of: Explain, WorkspaceSetup, DeepCodeReview, ImplementationPlan, CodePatch, BuildFix, TestFailureAnalysis, Diagnostic, ArchitectureReview, SecurityReview, RefactorPlan, Documentation, CommandPlan, Validation.
            confidence must be a number from 0 to 1.
            Do not add markdown, prose, code fences, or extra keys.
            """;
    }

    private static string BuildPrompt(JsonBenchmarkCase benchmarkCase)
    {
        return benchmarkCase.Prompt;
    }

    private static JsonNode? TryParseModelOutput(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineBreak = trimmed.IndexOf('\n');
            if (firstLineBreak >= 0)
            {
                trimmed = trimmed[(firstLineBreak + 1)..];
            }

            var endFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (endFence >= 0)
            {
                trimmed = trimmed[..endFence];
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var candidate = trimmed[start..(end + 1)];
        try
        {
            return JsonNode.Parse(candidate);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryReadDouble(JsonNode? node, out double value)
    {
        if (node is null)
        {
            value = default;
            return false;
        }

        if (node.GetValueKind() == JsonValueKind.Number && double.TryParse(node.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        return double.TryParse(node.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
    }

    private static IReadOnlyList<string> FilterModels(IReadOnlyList<OllamaModelSummary> allModels, IReadOnlyList<string>? filter)
    {
        if (filter is null || filter.Count == 0)
        {
            return allModels.Select(static model => model.Name).ToArray();
        }

        var allowed = new HashSet<string>(filter, StringComparer.OrdinalIgnoreCase);
        return allModels.Where(model => allowed.Contains(model.Name)).Select(static model => model.Name).ToArray();
    }

    private static OllamaModelBenchmarkResult SummarizeModel(string model, IReadOnlyList<OllamaBenchmarkCaseResult> caseResults)
    {
        var total = caseResults.Count;
        var correct = caseResults.Count(static result => result.Correct);
        var accuracy = total == 0 ? 0d : (double)correct / total;
        var averageConfidence = total == 0 ? 0d : caseResults.Average(static result => result.Confidence);
        var averageScore = total == 0 ? 0d : caseResults.Average(static result => result.Score);
        var averageLatency = total == 0 ? 0d : caseResults.Average(static result => result.ElapsedMilliseconds);

        return new OllamaModelBenchmarkResult(
            model,
            total,
            correct,
            accuracy,
            averageConfidence,
            averageScore,
            averageLatency,
            caseResults);
    }

    private void WriteHumanReadable(
        Uri baseUrl,
        IReadOnlyList<JsonBenchmarkCase> cases,
        IReadOnlyList<OllamaModelBenchmarkResult> modelResults)
    {
        _output.WriteLine(FormattableString.Invariant($"Base URL : {baseUrl}"));
        _output.WriteLine(FormattableString.Invariant($"Cases    : {cases.Count}"));
        _output.WriteLine(FormattableString.Invariant($"Models   : {modelResults.Count}"));
        _output.WriteLine();

        _output.WriteLine("Ranking:");
        for (var index = 0; index < modelResults.Count; index++)
        {
            var model = modelResults[index];
            _output.WriteLine(FormattableString.Invariant(
                $"{index + 1}. {model.Model}  score={model.AverageScore:0.000}  accuracy={(model.Accuracy * 100.0):0.0}%  confidence={model.AverageConfidence:0.000}"));
        }

        _output.WriteLine();
        foreach (var model in modelResults)
        {
            _output.WriteLine(FormattableString.Invariant($"Model: {model.Model}"));
            _output.WriteLine(FormattableString.Invariant($"  accuracy: {(model.Accuracy * 100.0):0.0}% ({model.CorrectCases}/{model.TotalCases})"));
            _output.WriteLine(FormattableString.Invariant($"  avg confidence: {model.AverageConfidence:0.000}"));
            _output.WriteLine(FormattableString.Invariant($"  avg score: {model.AverageScore:0.000}"));
            _output.WriteLine(FormattableString.Invariant($"  avg latency: {model.AverageLatencyMilliseconds:0} ms"));

            foreach (var result in model.CaseResults)
            {
                var status = result.Correct ? "OK" : "MISS";
                var suffix = string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : $"  error={result.ErrorMessage}";
                _output.WriteLine(FormattableString.Invariant(
                    $"    - {result.CaseId}: {status} expected={result.ExpectedActivity} actual={result.PredictedActivity} confidence={result.Confidence:0.000} score={result.Score:0.000} {result.ElapsedMilliseconds:0}ms{suffix}"));
            }

            _output.WriteLine();
        }
    }
}

internal sealed record JsonBenchmarkCase(
    string Id,
    string Prompt,
    string ExpectedActivity,
    string ExpectedSchemaName);

internal sealed record OllamaBenchmarkCaseResult(
    string CaseId,
    string Prompt,
    string ExpectedActivity,
    string ExpectedSchemaName,
    string PredictedActivity,
    string PredictedSchemaName,
    double Confidence,
    bool Correct,
    double Score,
    long ElapsedMilliseconds,
    string? ErrorMessage);

internal sealed record OllamaModelBenchmarkResult(
    string Model,
    int TotalCases,
    int CorrectCases,
    double Accuracy,
    double AverageConfidence,
    double AverageScore,
    double AverageLatencyMilliseconds,
    IReadOnlyList<OllamaBenchmarkCaseResult> CaseResults);

internal sealed record OllamaBenchResult(
    string BaseUrl,
    bool ServerReachable,
    string? Message,
    IReadOnlyList<OllamaModelBenchmarkResult> Models,
    IReadOnlyList<JsonBenchmarkCase> Cases);
