using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IMigrationProvider
    {
        ValueTask<Fin<Unit>> RunMigrationsAsync(CancellationToken ct);
    }
}