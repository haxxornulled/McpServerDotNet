using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueryBus
    {
        ValueTask<Fin<T>> SendAsync<T>(object query, CancellationToken ct);
    }
}