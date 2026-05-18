using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventProcessorProvider<TEvent> where TEvent : class
    {
        ValueTask<Fin<Unit>> ProcessAsync(TEvent @event, CancellationToken ct);
    }
}