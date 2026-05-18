using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISecurityPolicyService
    {
        ValueTask<Fin<bool>> IsAllowedAsync(string userId, string action, CancellationToken ct);
    }
}