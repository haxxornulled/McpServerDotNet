using LanguageExt;
using McpServer.Application.Web.Results;

namespace McpServer.Application.Web
{
    public interface IWebResultCache
    {
        ValueTask<Fin<FetchedPageResult?>> GetAsync(string key, CancellationToken ct);
        ValueTask<Fin<Unit>> SetAsync(string key, FetchedPageResult value, TimeSpan ttl, CancellationToken ct);
    }
}
