using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITracingService
    {
        ValueTask<Fin<Unit>> StartSpanAsync(string operationName, CancellationToken ct);
        ValueTask<Fin<Unit>> EndSpanAsync(CancellationToken ct);
    }
}