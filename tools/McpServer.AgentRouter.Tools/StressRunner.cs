using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class StressRunner
{
    private readonly HttpClient _httpClient;
    private readonly StressSettings _settings;
    private readonly ConsoleStressReporter _reporter;
    private readonly JsonSerializerOptions _jsonOptions;

    public StressRunner(HttpClient httpClient, StressSettings settings, ConsoleStressReporter reporter)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _jsonOptions = JsonOptions.CreateIndented();
    }

    public async Task<StressRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var runId = $"stress-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        var reportDirectory = ResolveReportDirectory(_settings.ReportRootPath, runId);
        Directory.CreateDirectory(reportDirectory);

        _reporter.WriteHeader(runId, _settings, reportDirectory);

        var preflight = await RunPreflightAsync(cancellationToken).ConfigureAwait(false);
        if (!preflight.Success)
        {
            var failedResult = new StressRunResult(runId, reportDirectory, new List<StressRequestResult>(), new List<StressWorkloadSummary>(), preflight.FailureCount);
            await StressReportWriter.WriteAsync(failedResult, cancellationToken).ConfigureAwait(false);
            return failedResult;
        }

        var results = new ConcurrentBag<StressRequestResult>();

        if (!_settings.SkipMcpCatalog)
        {
            await RunWorkloadAsync(
                "MCP catalog",
                _settings.McpCatalogRequests,
                _settings.McpCatalogConcurrency,
                (index, token) => InvokeMcpCatalogAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_settings.SkipMcpToolCalls)
        {
            await RunWorkloadAsync(
                "MCP tool call",
                _settings.McpToolCallRequests,
                _settings.McpToolCallConcurrency,
                (index, token) => InvokeMcpToolCallAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_settings.SkipShellExecution)
        {
            await RunWorkloadAsync(
                "Shell execution",
                _settings.ShellExecutionRequests,
                _settings.ShellExecutionConcurrency,
                (index, token) => InvokeShellExecutionAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (_settings.EnableSshExecution)
        {
            await RunWorkloadAsync(
                "SSH execution",
                _settings.SshExecutionRequests,
                _settings.SshExecutionConcurrency,
                (index, token) => InvokeSshExecutionAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_settings.SkipChat)
        {
            await RunWorkloadAsync(
                "Chat completion",
                _settings.ChatRequests,
                _settings.ChatConcurrency,
                (index, token) => InvokeChatCompletionAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_settings.SkipAgentRuns)
        {
            await RunWorkloadAsync(
                "Agent run",
                _settings.AgentRunRequests,
                _settings.AgentRunConcurrency,
                (index, token) => InvokeAgentRunAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_settings.SkipAgentLoops)
        {
            await RunWorkloadAsync(
                "Agent loop",
                _settings.AgentLoopRequests,
                _settings.AgentLoopConcurrency,
                (index, token) => InvokeAgentLoopAsync(index, token),
                results,
                cancellationToken).ConfigureAwait(false);
        }

        var orderedResults = results
            .OrderBy(static item => item.Workload, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Index)
            .ToList();

        var summaries = StressSummaryCalculator.Calculate(orderedResults);
        _reporter.WriteCombinedSummary(summaries);

        var totalFailures = orderedResults.Count(static item => !item.Success);
        var runResult = new StressRunResult(runId, reportDirectory, orderedResults, summaries, totalFailures);
        await StressReportWriter.WriteAsync(runResult, cancellationToken).ConfigureAwait(false);

        _reporter.WriteReportPaths(runResult);
        _reporter.WriteFinalResult(totalFailures);
        return runResult;
    }

    private async Task<PreflightResult> RunPreflightAsync(CancellationToken cancellationToken)
    {
        _reporter.WriteSection("Preflight");

        var failures = 0;

        failures += await ProbeGetAsync("AgentRouter /health", "/health", cancellationToken).ConfigureAwait(false) ? 0 : 1;
        failures += await ProbeGetAsync("AgentRouter /v1/models", "/v1/models", cancellationToken).ConfigureAwait(false) ? 0 : 1;

        var mcpToolsResponse = await HttpJson.GetAsync(_httpClient, Combine(_settings.RouterBaseUrl, "/agent/mcp/tools"), cancellationToken).ConfigureAwait(false);
        if (mcpToolsResponse.Success)
        {
            var toolCount = JsonFieldReader.GetInt32(mcpToolsResponse.Json, "toolCount") ?? 0;
            _reporter.WritePass("AgentRouter /agent/mcp/tools responded.");
            Console.WriteLine($"MCP tool count: {toolCount.ToString(CultureInfo.InvariantCulture)}");

            if (toolCount <= 0)
            {
                _reporter.WriteFail("MCP tool catalog returned zero tools.");
                failures++;
            }
        }
        else
        {
            _reporter.WriteFail($"AgentRouter /agent/mcp/tools failed. {mcpToolsResponse.ErrorMessage}");
            failures++;
        }

        return new PreflightResult(failures == 0, failures);
    }

    private async Task<bool> ProbeGetAsync(string name, string path, CancellationToken cancellationToken)
    {
        var response = await HttpJson.GetAsync(_httpClient, Combine(_settings.RouterBaseUrl, path), cancellationToken).ConfigureAwait(false);
        if (response.Success)
        {
            _reporter.WritePass($"{name} responded.");
            return true;
        }

        _reporter.WriteFail($"{name} failed. {response.ErrorMessage}");
        return false;
    }

    private async Task RunWorkloadAsync(
        string workload,
        int totalRequests,
        int concurrency,
        Func<int, CancellationToken, Task<StressRequestResult>> operation,
        ConcurrentBag<StressRequestResult> results,
        CancellationToken cancellationToken)
    {
        if (totalRequests <= 0)
        {
            _reporter.WriteInfo($"{workload} skipped because request count is {totalRequests.ToString(CultureInfo.InvariantCulture)}.");
            return;
        }

        _reporter.WriteWorkloadHeader(workload, totalRequests, concurrency);

        using var throttler = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new List<Task>(totalRequests);

        for (var index = 1; index <= totalRequests; index++)
        {
            var capturedIndex = index;
            tasks.Add(Task.Run(async () =>
            {
                await throttler.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await operation(capturedIndex, cancellationToken).ConfigureAwait(false);
                    results.Add(result);
                }
                finally
                {
                    throttler.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var workloadResults = results
            .Where(item => string.Equals(item.Workload, workload, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static item => item.Index)
            .ToList();

        _reporter.WriteWorkloadSummary(workload, StressSummaryCalculator.Calculate(workloadResults).Single());
        _reporter.WriteFailedResults(workloadResults);
    }

    private async Task<StressRequestResult> InvokeChatCompletionAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _settings.ChatModel,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Reply with exactly one word: ok"
                }
            },
            stream = false
        };

        return await InvokeJsonWorkloadAsync(
            "Chat completion",
            index,
            HttpMethod.Post,
            "/v1/chat/completions",
            body,
            json => JsonFieldReader.GetString(json, "choices", 0, "message", "content") ?? string.Empty,
            json => !string.IsNullOrWhiteSpace(JsonFieldReader.GetString(json, "choices", 0, "message", "content")),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeAgentRunAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _settings.ChatModel,
            goal = "Reply with exactly: stress agent ok"
        };

        return await InvokeJsonWorkloadAsync(
            "Agent run",
            index,
            HttpMethod.Post,
            "/agent/runs",
            body,
            json => JsonFieldReader.GetString(json, "result") ?? string.Empty,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeAgentLoopAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            goal = "List the MCP workspace root once and stop.",
            max_steps = 1,
            tool_name = "fs.list_directory",
            arguments = new
            {
                path = "."
            }
        };

        return await InvokeJsonWorkloadAsync(
            "Agent loop",
            index,
            HttpMethod.Post,
            "/agent/loops",
            body,
            CreateAgentLoopPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetArrayCount(json, "steps") ?? 0) > 0,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeMcpCatalogAsync(int index, CancellationToken cancellationToken)
    {
        return await InvokeJsonWorkloadAsync(
            "MCP catalog",
            index,
            HttpMethod.Get,
            "/agent/mcp/tools",
            null,
            CreateMcpCatalogPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "ok", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetInt32(json, "toolCount") ?? 0) > 0,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeMcpToolCallAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            toolName = "fs.list_directory",
            arguments = new
            {
                path = "."
            }
        };

        return await InvokeJsonWorkloadAsync(
            "MCP tool call",
            index,
            HttpMethod.Post,
            "/agent/mcp/tools/call",
            body,
            CreateMcpToolCallPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetBoolean(json, "allowed") ?? false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeShellExecutionAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            command = "dotnet",
            arguments = new[]
            {
                "--info"
            },
            working_directory = "."
        };

        return await InvokeJsonWorkloadAsync(
            "Shell execution",
            index,
            HttpMethod.Post,
            "/agent/shell/exec",
            body,
            CreateShellExecutionPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetBoolean(json, "allowed") ?? false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeSshExecutionAsync(int index, CancellationToken cancellationToken)
    {
        var body = new
        {
            profile = _settings.SshProfile,
            command = _settings.SshCommand,
            arguments = Array.Empty<string>(),
            working_directory = _settings.SshWorkingDirectory
        };

        return await InvokeJsonWorkloadAsync(
            "SSH execution",
            index,
            HttpMethod.Post,
            "/agent/ssh/exec",
            body,
            CreateSshExecutionPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetBoolean(json, "allowed") ?? false),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeJsonWorkloadAsync(
        string workload,
        int index,
        HttpMethod method,
        string path,
        object? body,
        Func<JsonNode?, string> previewFactory,
        Func<JsonNode?, bool> successPredicate,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        HttpJsonResponse response;

        try
        {
            var uri = Combine(_settings.RouterBaseUrl, path);
            response = method == HttpMethod.Get
                ? await HttpJson.GetAsync(_httpClient, uri, cancellationToken).ConfigureAwait(false)
                : await HttpJson.PostAsync(_httpClient, uri, body, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            stopwatch.Stop();
            return StressRequestResult.Failed(workload, index, 0, stopwatch.ElapsedMilliseconds, exception.Message, startedAtUtc);
        }

        stopwatch.Stop();

        var success = response.Success && successPredicate(response.Json);
        var preview = response.Json is null ? string.Empty : previewFactory(response.Json);
        var error = success ? null : response.ErrorMessage ?? "Unexpected response payload.";

        return new StressRequestResult
        {
            Workload = workload,
            Index = index,
            Success = success,
            StatusCode = response.StatusCode,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            Error = error,
            ContentPreview = preview,
            StartedAtUtc = startedAtUtc
        };
    }

    private static string CreateAgentLoopPreview(JsonNode? json)
    {
        var status = JsonFieldReader.GetString(json, "status") ?? string.Empty;
        var stepCount = JsonFieldReader.GetArrayCount(json, "steps") ?? 0;
        return FormattableString.Invariant($"status={status}; steps={stepCount}");
    }

    private static string CreateMcpCatalogPreview(JsonNode? json)
    {
        var toolCount = JsonFieldReader.GetInt32(json, "toolCount") ?? 0;
        return FormattableString.Invariant($"toolCount={toolCount}");
    }

    private static string CreateMcpToolCallPreview(JsonNode? json)
    {
        var traceId = JsonFieldReader.GetString(json, "traceId") ?? string.Empty;
        return FormattableString.Invariant($"traceId={traceId}");
    }

    private static string CreateShellExecutionPreview(JsonNode? json)
    {
        var traceId = JsonFieldReader.GetString(json, "trace_id") ?? string.Empty;
        var exitCode = JsonFieldReader.GetInt32(json, "exit_code");
        return FormattableString.Invariant($"traceId={traceId}; exitCode={exitCode}");
    }

    private static string CreateSshExecutionPreview(JsonNode? json)
    {
        var traceId = JsonFieldReader.GetString(json, "trace_id") ?? string.Empty;
        var exitCode = JsonFieldReader.GetInt32(json, "exit_code");
        var profile = JsonFieldReader.GetString(json, "profile") ?? string.Empty;
        return FormattableString.Invariant($"profile={profile}; traceId={traceId}; exitCode={exitCode}");
    }

    private static Uri Combine(Uri baseUri, string path)
    {
        return new Uri(baseUri, path);
    }

    private static string ResolveReportDirectory(string reportRootPath, string runId)
    {
        var root = Path.IsPathRooted(reportRootPath)
            ? reportRootPath
            : Path.Combine(Directory.GetCurrentDirectory(), reportRootPath);

        return Path.Combine(Path.GetFullPath(root), runId);
    }
}
