using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEventStoreProvider
    {
        ValueTask<Fin<Unit>> SaveAsync<TEvent>(TEvent @event, CancellationToken ct) where TEvent : class;
        ValueTask<Fin<IReadOnlyList<TEvent>>> LoadAsync<TEvent>(string aggregateId, CancellationToken ct) where TEvent : class;
    }
}