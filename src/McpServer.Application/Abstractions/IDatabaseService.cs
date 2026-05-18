using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IDatabaseService
    {
        ValueTask<Fin<T>> QueryAsync<T>(string sql, CancellationToken ct);
        ValueTask<Fin<Unit>> ExecuteAsync(string sql, CancellationToken ct);
    }
}