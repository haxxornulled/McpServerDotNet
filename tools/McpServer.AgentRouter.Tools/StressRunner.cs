using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class StressRunner
{
    private const string DefaultMcpToolCoverageWorkload = "MCP default tool coverage";
    private const string DefaultMcpToolCoverageFolder = "cli-tool-coverage";
    private const string DefaultMcpToolCoverageSourceFile = DefaultMcpToolCoverageFolder + "/source.txt";
    private const string DefaultMcpToolCoverageCopiedFile = DefaultMcpToolCoverageFolder + "/copy.txt";
    private const string DefaultMcpToolCoverageMovedFile = DefaultMcpToolCoverageFolder + "/moved.txt";

    private readonly HttpClient _httpClient;
    private readonly StressSettings _settings;
    private readonly ConsoleStressReporter _reporter;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _defaultMcpWorkspaceRoot;

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

        var results = new List<StressRequestResult>();

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

        if (_settings.EnableMcpDefaultToolCoverage)
        {
            await RunMcpDefaultToolCoverageAsync(results, cancellationToken).ConfigureAwait(false);
        }

        var orderedResults = results.ToArray();
        Array.Sort(orderedResults, StressRequestResultComparer.Instance);

        var summaries = StressSummaryCalculator.Calculate(orderedResults);
        _reporter.WriteCombinedSummary(summaries);

        var totalFailures = CountFailures(orderedResults);
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
            _reporter.WriteInfo($"MCP tool count: {toolCount.ToString(CultureInfo.InvariantCulture)}");

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
        List<StressRequestResult> results,
        CancellationToken cancellationToken)
    {
        if (totalRequests <= 0)
        {
            _reporter.WriteInfo($"{workload} skipped because request count is {totalRequests.ToString(CultureInfo.InvariantCulture)}.");
            return;
        }

        _reporter.WriteWorkloadHeader(workload, totalRequests, concurrency);

        var workerCount = concurrency < 1 ? 1 : Math.Min(concurrency, totalRequests);
        var workerResults = ArrayPool<StressRequestResult>.Shared.Rent(totalRequests);
        var workers = new Task[workerCount];
        var nextIndex = 0;

        try
        {
            for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                workers[workerIndex] = RunWorkerAsync(operation, cancellationToken);
            }

            await Task.WhenAll(workers).ConfigureAwait(false);

            var workloadResults = new List<StressRequestResult>(totalRequests);
            for (var index = 0; index < totalRequests; index++)
            {
                var result = workerResults[index];
                if (result is null)
                {
                    throw new InvalidOperationException($"Workload '{workload}' did not populate result {index + 1}.");
                }

                workloadResults.Add(result);
            }

            results.AddRange(workloadResults);

            var summaries = StressSummaryCalculator.Calculate(workloadResults);
            if (summaries.Count != 1)
            {
                throw new InvalidOperationException($"Workload '{workload}' produced an unexpected summary count.");
            }

            _reporter.WriteWorkloadSummary(workload, summaries[0]);
            _reporter.WriteFailedResults(workloadResults);
        }
        finally
        {
            ArrayPool<StressRequestResult>.Shared.Return(workerResults, clearArray: true);
        }

        async Task RunWorkerAsync(
            Func<int, CancellationToken, Task<StressRequestResult>> workerOperation,
            CancellationToken workerCancellationToken)
        {
            while (true)
            {
                var current = Interlocked.Increment(ref nextIndex);
                if (current > totalRequests)
                {
                    break;
                }

                var result = await workerOperation(current, workerCancellationToken).ConfigureAwait(false);
                workerResults[current - 1] = result;
            }
        }
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

    private async Task RunMcpDefaultToolCoverageAsync(
        List<StressRequestResult> results,
        CancellationToken cancellationToken)
    {
        _defaultMcpWorkspaceRoot = null;
        _reporter.WriteWorkloadHeader(DefaultMcpToolCoverageWorkload, DefaultMcpToolCoverageCases.Count, 1);

        var workloadResults = new List<StressRequestResult>(DefaultMcpToolCoverageCases.Count);

        for (var index = 1; index <= DefaultMcpToolCoverageCases.Count; index++)
        {
            var result = await InvokeMcpDefaultToolCoverageAsync(index, cancellationToken).ConfigureAwait(false);
            workloadResults.Add(result);
            results.Add(result);
        }

        var summaries = StressSummaryCalculator.Calculate(workloadResults);
        if (summaries.Count != 1)
        {
            throw new InvalidOperationException("Default MCP tool coverage produced an unexpected summary count.");
        }

        _reporter.WriteWorkloadSummary(DefaultMcpToolCoverageWorkload, summaries[0]);
        _reporter.WriteFailedResults(workloadResults);
    }

    private async Task<StressRequestResult> InvokeMcpDefaultToolCoverageAsync(int index, CancellationToken cancellationToken)
    {
        var scenario = DefaultMcpToolCoverageCases[index - 1];
        var workspaceRoot = _defaultMcpWorkspaceRoot;

        if (scenario.RequiresWorkspaceRoot && string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return StressRequestResult.Failed(
                DefaultMcpToolCoverageWorkload,
                index,
                0,
                0,
                "Default MCP workspace root was not resolved from workspace.status.",
                DateTimeOffset.UtcNow);
        }

        var body = new
        {
            toolName = scenario.ToolName,
            arguments = scenario.ArgumentsFactory(workspaceRoot)
        };

        Action<JsonNode?>? responseObserver = null;
        if (string.Equals(scenario.ToolName, "workspace.status", StringComparison.OrdinalIgnoreCase))
        {
            responseObserver = json =>
            {
                _defaultMcpWorkspaceRoot =
                    JsonFieldReader.GetString(json, "result", "structuredContent", "workspaceRoot")
                    ?? string.Empty;
            };
        }

        return await InvokeJsonWorkloadAsync(
            DefaultMcpToolCoverageWorkload,
            index,
            HttpMethod.Post,
            "/agent/mcp/tools/call",
            body,
            json => CreateMcpDefaultToolCoveragePreview(scenario.ToolName, json),
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetBoolean(json, "allowed") ?? false)
                && (!string.Equals(scenario.ToolName, "workspace.status", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(JsonFieldReader.GetString(json, "result", "structuredContent", "workspaceRoot"))),
            cancellationToken,
            responseObserver).ConfigureAwait(false);
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

        Action<JsonNode?>? responseObserver = _reporter.WriteSshExecutionResponse;

        return await InvokeJsonWorkloadAsync(
            "SSH execution",
            index,
            HttpMethod.Post,
            "/agent/ssh/exec",
            body,
            CreateSshExecutionPreview,
            json => string.Equals(JsonFieldReader.GetString(json, "status"), "completed", StringComparison.OrdinalIgnoreCase)
                && (JsonFieldReader.GetBoolean(json, "allowed") ?? false),
            cancellationToken,
            responseObserver).ConfigureAwait(false);
    }

    private async Task<StressRequestResult> InvokeJsonWorkloadAsync(
        string workload,
        int index,
        HttpMethod method,
        string path,
        object? body,
        Func<JsonNode?, string> previewFactory,
        Func<JsonNode?, bool> successPredicate,
        CancellationToken cancellationToken,
        Action<JsonNode?>? responseObserver = null)
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
        responseObserver?.Invoke(response.Json);

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

    private static string CreateMcpDefaultToolCoveragePreview(string toolName, JsonNode? json)
    {
        var traceId = JsonFieldReader.GetString(json, "traceId") ?? string.Empty;
        if (string.Equals(toolName, "workspace.status", StringComparison.OrdinalIgnoreCase))
        {
            var workspaceRoot = JsonFieldReader.GetString(json, "result", "structuredContent", "workspaceRoot") ?? string.Empty;
            return FormattableString.Invariant($"tool={toolName}; workspaceRoot={workspaceRoot}; traceId={traceId}");
        }

        return FormattableString.Invariant($"tool={toolName}; traceId={traceId}");
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

    private static int CountFailures(IReadOnlyList<StressRequestResult> results)
    {
        var failures = 0;
        for (var index = 0; index < results.Count; index++)
        {
            if (!results[index].Success)
            {
                failures++;
            }
        }

        return failures;
    }

    private sealed record McpDefaultToolCoverageCase(
        string ToolName,
        bool RequiresWorkspaceRoot,
        Func<string?, object> ArgumentsFactory);

    private static readonly IReadOnlyList<McpDefaultToolCoverageCase> DefaultMcpToolCoverageCases =
    [
        new("activity.schemas.list", false, static _ => new { }),
        new("activity.route", false, static _ => new
        {
            request = "Diagnose a build failure in this repository."
        }),
        new("activity.context.preview", false, static _ => new
        {
            request = "Explain the current isolated integration workspace.",
            activity = "explain",
            maxContextBytes = 4000
        }),
        new("activity.run", false, static _ => new
        {
            request = "Explain the current isolated integration workspace.",
            activity = "explain",
            maxContextBytes = 4000,
            runBuild = false,
            runTests = false
        }),
        new("workspace.status", false, static _ => new { }),
        new("workspace.open", true, static root => new { path = root }),
        new("workspace.set_root", true, static root => new { path = root }),
        new("fs.create_directory", true, static _ => new { path = DefaultMcpToolCoverageFolder }),
        new("fs.write_text", true, static _ => new
        {
            path = DefaultMcpToolCoverageSourceFile,
            content = "hello",
            overwrite = true
        }),
        new("fs.append_text", true, static _ => new
        {
            path = DefaultMcpToolCoverageSourceFile,
            content = Environment.NewLine + "world",
            flush = true
        }),
        new("fs.read_text", true, static _ => new { path = DefaultMcpToolCoverageSourceFile }),
        new("fs.read_file", true, static _ => new { path = DefaultMcpToolCoverageSourceFile }),
        new("fs.get_metadata", true, static _ => new { path = DefaultMcpToolCoverageSourceFile }),
        new("fs.list_directory", true, static _ => new { path = DefaultMcpToolCoverageFolder }),
        new("fs.copy_path", true, static _ => new
        {
            source_path = DefaultMcpToolCoverageSourceFile,
            destination_path = DefaultMcpToolCoverageCopiedFile,
            overwrite = true
        }),
        new("fs.move_path", true, static _ => new
        {
            source_path = DefaultMcpToolCoverageCopiedFile,
            destination_path = DefaultMcpToolCoverageMovedFile,
            overwrite = true
        }),
        new("fs.delete_path", true, static _ => new
        {
            path = DefaultMcpToolCoverageMovedFile,
            recursive = false
        }),
        new("workspace.select_folder", true, static _ => new { path = DefaultMcpToolCoverageFolder }),
        new("workspace.inspect", true, static _ => new
        {
            path = ".",
            maxDepth = 1,
            maxFiles = 8,
            maxFileBytes = 4096,
            maxTotalFileBytes = 8192
        }),
        new("workspace.set_root", true, static root => new { path = root }),
        new("fs.delete_path", true, static _ => new
        {
            path = DefaultMcpToolCoverageFolder,
            recursive = true
        })
    ];
}
