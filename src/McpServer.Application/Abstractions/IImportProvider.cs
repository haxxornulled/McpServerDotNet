using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IImportProvider
    {
        ValueTask<Fin<IReadOnlyList<T>>> ImportAsync<T>(string fileName, CancellationToken ct);
    }
}