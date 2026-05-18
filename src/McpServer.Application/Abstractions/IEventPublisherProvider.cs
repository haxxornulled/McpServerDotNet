using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventPublisherProvider
    {
        ValueTask<Fin<Unit>> PublishAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : class;
    }
}