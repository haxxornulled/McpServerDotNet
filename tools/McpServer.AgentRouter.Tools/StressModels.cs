using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
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
        return results
            .GroupBy(static item => item.Workload, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(CreateSummary)
            .ToList();
    }

    private static StressWorkloadSummary CreateSummary(IGrouping<string, StressRequestResult> group)
    {
        var values = group.Select(static item => (double)item.ElapsedMilliseconds).Order().ToArray();
        var successes = group.Count(static item => item.Success);
        var failures = group.Count(static item => !item.Success);

        return new StressWorkloadSummary
        {
            Workload = group.Key,
            Total = group.Count(),
            Success = successes,
            Failed = failures,
            MinMs = values.Length == 0 ? 0 : Math.Round(values[0], 2),
            AvgMs = values.Length == 0 ? 0 : Math.Round(values.Average(), 2),
            P50Ms = Percentile(values, 50),
            P95Ms = Percentile(values, 95),
            MaxMs = values.Length == 0 ? 0 : Math.Round(values[^1], 2)
        };
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return Math.Round(sortedValues[0], 2);
        }

        var rank = (percentile / 100.0d) * (sortedValues.Count - 1);
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
    public void WriteHeader(string stressRunId, StressSettings settings, string reportDirectory)
    {
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
        ConsoleWriter.WriteSection($"{workload} workload");
        Console.WriteLine($"Requests:    {requests.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Concurrency: {concurrency.ToString(CultureInfo.InvariantCulture)}");
    }

    public void WriteWorkloadSummary(string workload, StressWorkloadSummary summary)
    {
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
        var failedResults = results
            .Where(static item => !item.Success)
            .OrderBy(static item => item.Index)
            .ToList();

        if (failedResults.Count == 0)
        {
            return;
        }

        ConsoleWriter.WriteWarning("Failed request details:");

        foreach (var result in failedResults)
        {
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
        ConsoleWriter.WriteSection("Combined summary");
        Console.WriteLine("Workload                  Total  Success  Failed  AvgMs  P50Ms  P95Ms  MaxMs");
        Console.WriteLine("--------                  -----  -------  ------  -----  -----  -----  -----");

        foreach (var summary in summaries)
        {
            Console.WriteLine(
                FormattableString.Invariant($"{summary.Workload,-24} {summary.Total,5}  {summary.Success,7}  {summary.Failed,6}  {summary.AvgMs,5}  {summary.P50Ms,5}  {summary.P95Ms,5}  {summary.MaxMs,5}"));
        }
    }

    public void WriteReportPaths(StressRunResult runResult)
    {
        Console.WriteLine("Reports written:");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "results.json")}");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "summary.json")}");
        Console.WriteLine($"  {Path.Combine(runResult.ReportDirectory, "results.csv")}");
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
}
