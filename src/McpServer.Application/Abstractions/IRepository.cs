using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IRepository<T>
    {
        ValueTask<Fin<T>> GetByIdAsync(object id, CancellationToken ct);
        ValueTask<Fin<Unit>> CreateAsync(T entity, CancellationToken ct);
        ValueTask<Fin<Unit>> UpdateAsync(T entity, CancellationToken ct);
        ValueTask<Fin<Unit>> DeleteAsync(object id, CancellationToken ct);
    }
}