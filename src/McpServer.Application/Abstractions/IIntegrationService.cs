using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IIntegrationService
    {
        ValueTask<Fin<Unit>> SendAsync<T>(string endpoint, T data, CancellationToken ct);
        ValueTask<Fin<T>> ReceiveAsync<T>(string endpoint, CancellationToken ct);
    }
}