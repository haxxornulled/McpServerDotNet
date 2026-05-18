using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISecurityPolicyProvider
    {
        ValueTask<Fin<bool>> IsAllowedAsync(string userId, string action, CancellationToken ct);
    }
}