using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IWebhookProcessor<T>
    {
        ValueTask<Fin<Unit>> ProcessAsync(T payload, CancellationToken ct);
    }
}