using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueryHandler<TQuery, TResult>
    {
        ValueTask<Fin<TResult>> HandleAsync(TQuery query, CancellationToken ct);
    }
}