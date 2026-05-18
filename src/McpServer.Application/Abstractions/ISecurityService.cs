using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISecurityService
    {
        ValueTask<Fin<Unit>> ValidateSecurityAsync(CancellationToken ct);
    }
}