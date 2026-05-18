using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventProcessor<TEvent> where TEvent : class
    {
        ValueTask<Fin<Unit>> ProcessAsync(TEvent @event, CancellationToken ct);
    }
}