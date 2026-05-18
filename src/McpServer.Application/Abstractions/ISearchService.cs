using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISearchService
    {
        ValueTask<Fin<IReadOnlyList<T>>> SearchAsync<T>(string query, CancellationToken ct);
    }
}