using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueryProvider
    {
        ValueTask<Fin<T>> ExecuteAsync<T>(string query, IDictionary<string, object> parameters, CancellationToken ct);
    }
}