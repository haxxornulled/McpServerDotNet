using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueryBusProvider
    {
        ValueTask<Fin<T>> SendAsync<T>(object query, CancellationToken ct);
    }
}