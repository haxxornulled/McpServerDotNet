using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISubscriptionService
    {
        ValueTask<Fin<Unit>> SubscribeAsync(string topic, string endpoint, CancellationToken ct);
        ValueTask<Fin<Unit>> UnsubscribeAsync(string topic, string endpoint, CancellationToken ct);
    }
}