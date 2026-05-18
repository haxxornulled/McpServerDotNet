using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISearchProvider
    {
        ValueTask<Fin<IReadOnlyList<T>>> SearchAsync<T>(string query, CancellationToken ct);
    }
}