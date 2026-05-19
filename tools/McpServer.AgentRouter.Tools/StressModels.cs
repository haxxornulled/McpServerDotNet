using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class StressRequestResult
{
    public string Workload { get; init; } = string.Empty;

    public int Index { get; init; }

    public bool Success { get; init; }

    public int StatusCode { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public string? Error { get; init; }

    public string? ContentPreview { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public static StressRequestResult Failed(string workload, int index, int statusCode, long elapsedMilliseconds, string error, DateTimeOffset startedAtUtc)
    {
        return new StressRequestResult
        {
            Workload = workload,
            Index = index,
            Success = false,
            StatusCode = statusCode,
            ElapsedMilliseconds = elapsedMilliseconds,
            Error = error,
            StartedAtUtc = startedAtUtc
        };
    }
}

internal sealed class StressWorkloadSummary
{
    public string Workload { get; init; } = string.Empty;

    public int Total { get; init; }

    public int Success { get; init; }

    public int Failed { get; init; }

    public double MinMs { get; init; }

    public double AvgMs { get; init; }

    public double P50Ms { get; init; }

    public double P95Ms { get; init; }

    public double MaxMs { get; init; }
}

internal sealed class StressRunResult
{
    public StressRunResult(
        string stressRunId,
        string reportDirectory,
        IReadOnlyList<StressRequestResult> results,
        IReadOnlyList<StressWorkloadSummary> summaries,
        int totalFailures)
    {
        StressRunId = stressRunId;
        ReportDirectory = reportDirectory;
        Results = results;
        Summaries = summaries;
        TotalFailures = totalFailures;
    }

    public string StressRunId { get; }

    public string ReportDirectory { get; }

    public IReadOnlyList<StressRequestResult> Results { get; }

    public IReadOnlyList<StressWorkloadSummary> Summaries { get; }

    public int TotalFailures { get; }
}

internal sealed class PreflightResult
{
    public PreflightResult(bool success, int failureCount)
    {
        Success = success;
        FailureCount = failureCount;
    }

    public bool Success { get; }

    public int FailureCount { get; }
}

internal static class StressSummaryCalculator
{
    public static IReadOnlyList<StressWorkloadSummary> Calculate(IReadOnlyList<StressRequestResult> results)
    {
        if (results.Count == 0)
        {
            return Array.Empty<StressWorkloadSummary>();
        }

        var accumulators = new Dictionary<string, WorkloadAccumulator>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            if (!accumulators.TryGetValue(result.Workload, out var accumulator))
            {
                accumulator = new WorkloadAccumulator();
                accumulators.Add(result.Workload, accumulator);
            }

            accumulator.Add(result.ElapsedMilliseconds, result.Success);
        }

        var workloadNames = ArrayPool<string>.Shared.Rent(accumulators.Count);
        var workloadIndex = 0;
        foreach (var workloadName in accumulators.Keys)
        {
            workloadNames[workloadIndex++] = workloadName;
        }

        Array.Sort(workloadNames, 0, workloadIndex, StringComparer.OrdinalIgnoreCase);

        var summaries = new StressWorkloadSummary[workloadIndex];
        try
        {
            for (var index = 0; index < workloadIndex; index++)
            {
                var workloadName = workloadNames[index];
                summaries[index] = accumulators[workloadName].ToSummary(workloadName);
            }

            return summaries;
        }
        finally
        {
            foreach (var accumulator in accumulators.Values)
            {
                accumulator.Dispose();
            }

            ArrayPool<string>.Shared.Return(workloadNames, clearArray: true);
        }
    }

    private sealed class WorkloadAccumulator : IDisposable
    {
        private double[] _elapsedValues = ArrayPool<double>.Shared.Rent(16);
        private int _count;
        private int _success;
        private int _failed;

        public void Add(long elapsedMilliseconds, bool success)
        {
            if (_count == _elapsedValues.Length)
            {
                Grow();
            }

            _elapsedValues[_count++] = elapsedMilliseconds;

            if (success)
            {
                _success++;
            }
            else
            {
                _failed++;
            }
        }

        public StressWorkloadSummary ToSummary(string workload)
        {
            if (_count == 0)
            {
                return new StressWorkloadSummary
                {
                    Workload = workload
                };
            }

            Array.Sort(_elapsedValues, 0, _count);

            double total = 0;
            for (var index = 0; index < _count; index++)
            {
                total += _elapsedValues[index];
            }

            return new StressWorkloadSummary
            {
                Workload = workload,
                Total = _count,
                Success = _success,
                Failed = _failed,
                MinMs = Math.Round(_elapsedValues[0], 2),
                AvgMs = Math.Round(total / _count, 2),
                P50Ms = Percentile(_elapsedValues, _count, 50),
                P95Ms = Percentile(_elapsedValues, _count, 95),
                MaxMs = Math.Round(_elapsedValues[_count - 1], 2)
            };
        }

        public void Dispose()
        {
            ArrayPool<double>.Shared.Return(_elapsedValues, clearArray: true);
        }

        private void Grow()
        {
            var next = ArrayPool<double>.Shared.Rent(_elapsedValues.Length * 2);
            Array.Copy(_elapsedValues, next, _count);
            ArrayPool<double>.Shared.Return(_elapsedValues, clearArray: true);
            _elapsedValues = next;
        }

        private static double Percentile(double[] sortedValues, int count, double percentile)
        {
            if (count == 0)
            {
                return 0;
            }

            if (count == 1)
            {
                return Math.Round(sortedValues[0], 2);
            }

            var rank = (percentile / 100.0d) * (count - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);

            if (lower == upper)
            {
                return Math.Round(sortedValues[lower], 2);
            }

            var weight = rank - lower;
            var value = (sortedValues[lower] * (1.0d - weight)) + (sortedValues[upper] * weight);
            return Math.Round(value, 2);
        }
    }
}

internal static class StressReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = McpServer.AgentRouter.Tools.JsonOptions.CreateIndented();

    public static async Task WriteAsync(StressRunResult runResult, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(runResult.ReportDirectory);

        var resultsPath = Path.Combine(runResult.ReportDirectory, "results.json");
        var summaryPath = Path.Combine(runResult.ReportDirectory, "summary.json");
        var csvPath = Path.Combine(runResult.ReportDirectory, "results.csv");
        var readmePath = Path.Combine(runResult.ReportDirectory, "README.txt");

        await File.WriteAllTextAsync(resultsPath, JsonSerializer.Serialize(runResult.Results, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(runResult.Summaries, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(csvPath, CsvWriter.Write(runResult.Results), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(readmePath, CreateReadme(runResult), cancellationToken).ConfigureAwait(false);
    }

    private static string CreateReadme(StressRunResult runResult)
    {
        return $"""
            AgentRouter stress run
            Run id: {runResult.StressRunId}
            Created UTC: {DateTimeOffset.UtcNow:O}

            Files:
            - results.json: per-request result data
            - summary.json: workload aggregate data
            - results.csv: per-request result data for spreadsheets
            """;
    }
}

internal static class CsvWriter
{
    public static string Write(IReadOnlyList<StressRequestResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Workload,Index,Success,StatusCode,ElapsedMilliseconds,Error,ContentPreview,StartedAtUtc");

        foreach (var result in results)
        {
            builder
                .Append(Escape(result.Workload)).Append(',')
                .Append(result.Index.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.Success.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(result.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(result.Error)).Append(',')
                .Append(Escape(result.ContentPreview)).Append(',')
                .Append(Escape(result.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture)))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(",", StringComparison.Ordinal) ||
               escaped.Contains("\n", StringComparison.Ordinal) ||
               escaped.Contains("\r", StringComparison.Ordinal)
            ? $"\"{escaped}\""
            : escaped;
    }
}

internal sealed class ConsoleStressReporter
{
    private readonly object _consoleLock = new();

    public void WriteHeader(string stressRunId, StressSettings settings, string reportDirectory)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        ConsoleWriter.WriteSection("AgentRouter stress harness");
        Console.WriteLine($"StressRunId:        {stressRunId}");
        Console.WriteLine($"RouterBaseUrl:      {settings.RouterBaseUrl}");
        Console.WriteLine($"ChatModel:          {settings.ChatModel}");
        Console.WriteLine($"ReportDirectory:    {reportDirectory}");
        Console.WriteLine($"TimeoutSeconds:     {settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)}");
        if (settings.EnableMcpDefaultToolCoverage)
        {
            Console.WriteLine("MCP default coverage: enabled");
        }
        if (settings.EnableSshExecution)
        {
            Console.WriteLine($"SSH workload:       enabled profile={settings.SshProfile} command={settings.SshCommand}");
        }
    }

    public void WriteSection(string name)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        ConsoleWriter.WriteSection(name);
    }

    public void WritePass(string message)
    {
        ConsoleWriter.WritePass(message);
    }

    public void WriteFail(string message)
    {
        ConsoleWriter.WriteError(message);
    }

    public void WriteInfo(string message)
    {
        ConsoleWriter.WriteInfo(message);
    }

    public void WriteWorkloadHeader(string workload, int requests, int concurrency)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        ConsoleWriter.WriteSection($"{workload} workload");
        Console.WriteLine($"Requests:    {requests.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Concurrency: {concurrency.ToString(CultureInfo.InvariantCulture)}");
    }

    public void WriteWorkloadSummary(string workload, StressWorkloadSummary summary)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Console.WriteLine($"Workload: {workload}");
        Console.WriteLine($"  Total:   {summary.Total.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  Success: {summary.Success.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  Failed:  {summary.Failed.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  AvgMs:   {summary.AvgMs.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  P50Ms:   {summary.P50Ms.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  P95Ms:   {summary.P95Ms.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"  MaxMs:   {summary.MaxMs.ToString(CultureInfo.InvariantCulture)}");

        if (summary.Failed == 0)
        {
            ConsoleWriter.WritePass($"{workload} completed {summary.Total.ToString(CultureInfo.InvariantCulture)} requests with 0 failures.");
        }
        else
        {
            ConsoleWriter.WriteError($"{workload} had {summary.Failed.ToString(CultureInfo.InvariantCulture)} failures out of {summary.Total.ToString(CultureInfo.InvariantCulture)} requests.");
        }
    }

    public void WriteFailedResults(IReadOnlyList<StressRequestResult> results)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        var failedCount = 0;
        for (var index = 0; index < results.Count; index++)
        {
            if (!results[index].Success)
            {
                failedCount++;
            }
        }

        if (failedCount == 0)
        {
            return;
        }

        var failedResults = ArrayPool<StressRequestResult>.Shared.Rent(failedCount);
        var failedIndex = 0;
        try
        {
            for (var index = 0; index < results.Count; index++)
            {
                var result = results[index];
                if (result.Success)
                {
                    continue;
                }

                failedResults[failedIndex++] = result;
            }

            Array.Sort(failedResults, 0, failedIndex, StressRequestResultComparer.Instance);

            ConsoleWriter.WriteWarning("Failed request details:");

            for (var index = 0; index < failedIndex; index++)
            {
                var result = failedResults[index];
                Console.WriteLine(FormattableString.Invariant(
                    $"  #{result.Index} status={result.StatusCode} elapsedMs={result.ElapsedMilliseconds}"));

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    Console.WriteLine($"    error: {TrimForConsole(result.Error)}");
                }

                if (!string.IsNullOrWhiteSpace(result.ContentPreview))
                {
                    Console.WriteLine($"    preview: {TrimForConsole(result.ContentPreview)}");
                }
            }
        }
        finally
        {
            ArrayPool<StressRequestResult>.Shared.Return(failedResults, clearArray: true);
        }
    }

    private static string TrimForConsole(string value)
    {
        const int maxLength = 1000;

        var normalized = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        return normalized.Length <= maxLength
            ? normalized
            : string.Concat(normalized.AsSpan(0, maxLength), "...");
    }

    public void WriteCombinedSummary(IReadOnlyList<StressWorkloadSummary> summaries)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        ConsoleWriter.WriteSection("Combined summary");
        Console.WriteLine("Workload                  Total  Success  Failed  AvgMs  P50Ms  P95Ms  MaxMs");
        Console.WriteLine("--------                  -----  -------  ------  -----  -----  -----  -----");

        for (var index = 0; index < summaries.Count; index++)
        {
            var summary = summaries[index];
            Console.WriteLine(
                FormattableString.Invariant($"{summary.Workload,-24} {summary.Total,5}  {summary.Success,7}  {summary.Failed,6}  {summary.AvgMs,5}  {summary.P50Ms,5}  {summary.P95Ms,5}  {summary.MaxMs,5}"));
        }
    }

    public void WriteReportPaths(StressRunResult runResult)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Console.WriteLine("Reports written:");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "results.json")}");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "summary.json")}");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "results.csv")}");
    }

    public void WriteSshExecutionResponse(JsonNode? json)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        if (json is null)
        {
            return;
        }

        var profile = JsonFieldReader.GetString(json, "profile") ?? string.Empty;
        var host = JsonFieldReader.GetString(json, "host") ?? string.Empty;
        var traceId = JsonFieldReader.GetString(json, "trace_id") ?? string.Empty;
        var exitCode = JsonFieldReader.GetInt32(json, "exit_code");
        var timedOut = JsonFieldReader.GetBoolean(json, "timed_out") ?? false;

        lock (_consoleLock)
        {
            ConsoleWriter.WriteSection($"SSH response {profile}@{host}");
            Console.WriteLine($"TraceId:            {traceId}");
            Console.WriteLine($"ExitCode:           {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");
            Console.WriteLine($"TimedOut:           {timedOut.ToString(CultureInfo.InvariantCulture)}");
            WriteJsonBlock("Response JSON", json);
        }
    }

    public void WriteFinalResult(int totalFailures)
    {
        ConsoleWriter.WriteSection("Stress result");
        if (totalFailures == 0)
        {
            ConsoleWriter.WritePass("Stress run completed with zero request failures.");
            return;
        }

        ConsoleWriter.WriteError($"Stress run completed with {totalFailures.ToString(CultureInfo.InvariantCulture)} request failures.");
    }

    private static void WriteJsonBlock(string label, JsonNode? json)
    {
        Console.WriteLine($"{label}:");
        if (json is null)
        {
            Console.WriteLine("  <empty>");
            return;
        }

        var formatted = json.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var lines = formatted.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            Console.WriteLine($"  {lines[index].TrimEnd('\r')}");
        }
    }
}

internal sealed class StressRequestResultComparer : IComparer<StressRequestResult>
{
    public static readonly StressRequestResultComparer Instance = new();

    private StressRequestResultComparer()
    {
    }

    public int Compare(StressRequestResult? x, StressRequestResult? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var workloadComparison = string.Compare(x.Workload, y.Workload, StringComparison.OrdinalIgnoreCase);
        if (workloadComparison != 0)
        {
            return workloadComparison;
        }

        return x.Index.CompareTo(y.Index);
    }
}
