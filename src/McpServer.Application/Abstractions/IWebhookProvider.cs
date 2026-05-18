using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IWebhookProvider
    {
        ValueTask<Fin<Unit>> TriggerAsync(string webhookUrl, IDictionary<string, object> payload, CancellationToken ct);
    }
}