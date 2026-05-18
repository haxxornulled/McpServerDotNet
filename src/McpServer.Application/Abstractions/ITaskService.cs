using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITaskService
    {
        ValueTask<Fin<Unit>> ExecuteAsync<T>(T task, CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<T>>> GetActiveTasksAsync<T>(CancellationToken ct);
    }
}