using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IExportService
    {
        ValueTask<Fin<string>> ExportAsync<T>(IReadOnlyList<T> data, string format, CancellationToken ct);
    }
}