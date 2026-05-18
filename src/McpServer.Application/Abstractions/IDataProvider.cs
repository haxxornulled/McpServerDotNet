using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IDataProvider
    {
        ValueTask<Fin<T>> GetAsync<T>(string key, CancellationToken ct);
        ValueTask<Fin<Unit>> SetAsync<T>(string key, T value, CancellationToken ct);
        ValueTask<Fin<Unit>> DeleteAsync(string key, CancellationToken ct);
    }
}