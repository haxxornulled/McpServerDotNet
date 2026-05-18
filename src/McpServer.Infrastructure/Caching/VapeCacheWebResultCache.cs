using LanguageExt;
using McpServer.Application.Web;
using VapeCache.Abstractions.Caching;

using FetchedPageResult = McpServer.Application.Web.Results.FetchedPageResult;

namespace McpServer.Infrastructure.Caching;

public sealed class VapeCacheWebResultCache : IWebResultCache
{
    private readonly IVapeCache _cache;

    public VapeCacheWebResultCache(IVapeCache cache) => _cache = cache;

    public async ValueTask<Fin<FetchedPageResult?>> GetAsync(string key, CancellationToken ct)
    {
        var cacheKey = CacheKey<FetchedPageResult>.From(key);
        var result = await _cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
        return Fin<FetchedPageResult?>.Succ(result);
    }

    public async ValueTask<Fin<Unit>> SetAsync(string key, FetchedPageResult value, TimeSpan ttl, CancellationToken ct)
    {
        var cacheKey = CacheKey<FetchedPageResult>.From(key);
        await _cache.SetAsync(cacheKey, value, new CacheEntryOptions(ttl), ct).ConfigureAwait(false);
        return Fin<Unit>.Succ(Unit.Default);
    }
}
