using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISecurityProvider
    {
        ValueTask<Fin<Unit>> ValidateSecurityAsync(CancellationToken ct);
    }
}