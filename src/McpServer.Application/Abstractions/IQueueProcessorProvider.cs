using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IQueueProcessorProvider<T>
    {
        ValueTask<Fin<Unit>> ProcessAsync(T message, CancellationToken ct);
    }
}