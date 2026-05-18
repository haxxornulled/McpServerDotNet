using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IImportService
    {
        ValueTask<Fin<IReadOnlyList<T>>> ImportAsync<T>(string fileName, CancellationToken ct);
    }
}