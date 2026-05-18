using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IMonitoringService
    {
        ValueTask<Fin<Unit>> LogAsync(string message, CancellationToken ct);
        ValueTask<Fin<Unit>> AlertAsync(string message, CancellationToken ct);
    }
}