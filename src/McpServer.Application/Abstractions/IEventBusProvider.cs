using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventBusProvider
    {
        ValueTask<Fin<Unit>> PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : class;
        ValueTask<Fin<Unit>> SubscribeAsync<TEvent>(Func<TEvent, CancellationToken, Task<Fin<Unit>>> handler, CancellationToken ct) where TEvent : class;
    }
}