using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IMigrationService
    {
        ValueTask<Fin<Unit>> RunMigrationsAsync(CancellationToken ct);
    }
}