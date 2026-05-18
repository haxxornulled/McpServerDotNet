using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueueService
    {
        ValueTask<Fin<Unit>> EnqueueAsync<T>(T message, CancellationToken ct);
        ValueTask<Fin<T>> DequeueAsync<T>(CancellationToken ct);
    }
}