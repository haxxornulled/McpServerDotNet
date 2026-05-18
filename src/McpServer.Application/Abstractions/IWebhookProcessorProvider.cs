using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IWebhookProcessorProvider<T>
    {
        ValueTask<Fin<Unit>> ProcessAsync(T payload, CancellationToken ct);
    }
}