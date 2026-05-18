using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IUnitOfWork : IDisposable
    {
        ValueTask<Fin<Unit>> CommitAsync(CancellationToken ct);
        ValueTask<Fin<Unit>> RollbackAsync(CancellationToken ct);
    }
}