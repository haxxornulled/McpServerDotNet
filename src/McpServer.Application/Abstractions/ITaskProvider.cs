using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITaskProvider
    {
        ValueTask<Fin<Unit>> ExecuteAsync<T>(T task, CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<T>>> GetActiveTasksAsync<T>(CancellationToken ct);
    }
}