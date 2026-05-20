using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal static class OllamaBenchReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = McpServer.AgentRouter.Tools.JsonOptions.CreateIndented();

    public static async Task WriteAsync(
        OllamaBenchSettings settings,
        Uri baseUrl,
        bool serverReachable,
        string? message,
        IReadOnlyList<JsonBenchmarkCase> cases,
        IReadOnlyList<OllamaModelBenchmarkResult> models,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(models);

        var reportDirectory = Path.GetFullPath(settings.ReportDirectory);
        Directory.CreateDirectory(reportDirectory);

        var summaryJsonPath = Path.Combine(reportDirectory, "summary.json");
        var summaryCsvPath = Path.Combine(reportDirectory, "summary.csv");

        var summary = new
        {
            baseUrl = baseUrl.ToString(),
            promptSet = settings.PromptSet,
            serverReachable,
            message,
            modelCount = models.Count,
            caseCount = cases.Count,
            models = models,
            cases
        };

        await File.WriteAllTextAsync(summaryJsonPath, JsonSerializer.Serialize(summary, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(summaryCsvPath, OllamaBenchCsvWriter.Write(BuildSummaryRows(baseUrl, settings.PromptSet, models)), cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<OllamaBenchSummaryRow> BuildSummaryRows(
        Uri baseUrl,
        string promptSet,
        IReadOnlyList<OllamaModelBenchmarkResult> models)
    {
        var rows = new OllamaBenchSummaryRow[models.Count];
        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            rows[index] = new OllamaBenchSummaryRow(
                baseUrl.ToString(),
                promptSet,
                index + 1,
                model.Model,
                model.TotalCases,
                model.CorrectCases,
                model.Accuracy,
                model.AverageConfidence,
                model.AverageScore,
                model.AverageLatencyMilliseconds);
        }

        return rows;
    }

}

internal static class OllamaBenchCsvWriter
{
    public static string Write(IReadOnlyList<OllamaBenchSummaryRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BaseUrl,PromptSet,Rank,Model,TotalCases,CorrectCases,Accuracy,AverageConfidence,AverageScore,AverageLatencyMilliseconds");

        foreach (var row in rows)
        {
            builder
                .Append(Escape(row.BaseUrl)).Append(',')
                .Append(Escape(row.PromptSet)).Append(',')
                .Append(row.Rank.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.Model)).Append(',')
                .Append(row.TotalCases.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CorrectCases.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Accuracy.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AverageConfidence.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AverageScore.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.AverageLatencyMilliseconds.ToString("0.0", CultureInfo.InvariantCulture))
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

internal sealed record OllamaBenchSummaryRow(
    string BaseUrl,
    string PromptSet,
    int Rank,
    string Model,
    int TotalCases,
    int CorrectCases,
    double Accuracy,
    double AverageConfidence,
    double AverageScore,
    double AverageLatencyMilliseconds);
