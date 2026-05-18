using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IHealthCheckService
    {
        ValueTask<Fin<bool>> IsHealthyAsync(CancellationToken ct);
    }
}