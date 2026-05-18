using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITracingProvider
    {
        ValueTask<Fin<Unit>> StartSpanAsync(string operationName, CancellationToken ct);
        ValueTask<Fin<Unit>> EndSpanAsync(CancellationToken ct);
    }
}