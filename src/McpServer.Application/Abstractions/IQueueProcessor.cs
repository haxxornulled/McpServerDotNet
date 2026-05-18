using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueueProcessor<T>
    {
        ValueTask<Fin<Unit>> ProcessAsync(T message, CancellationToken ct);
    }
}