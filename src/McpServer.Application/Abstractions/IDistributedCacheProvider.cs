using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IDistributedCacheProvider
    {
        ValueTask<Fin<T>> GetAsync<T>(string key, CancellationToken ct);
        ValueTask<Fin<Unit>> SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct);
        ValueTask<Fin<Unit>> RemoveAsync(string key, CancellationToken ct);
    }
}