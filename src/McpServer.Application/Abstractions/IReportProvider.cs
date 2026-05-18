using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IReportProvider
    {
        ValueTask<Fin<string>> GenerateReportAsync(string reportType, IDictionary<string, object> parameters, CancellationToken ct);
    }
}