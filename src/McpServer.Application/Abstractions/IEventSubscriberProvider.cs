using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventSubscriberProvider<TEvent> where TEvent : class
    {
        ValueTask<Fin<Unit>> HandleAsync(TEvent @event, CancellationToken ct);
    }
}