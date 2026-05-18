using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventSubscriber<TEvent> where TEvent : class
    {
        ValueTask<Fin<Unit>> HandleAsync(TEvent @event, CancellationToken ct);
    }
}