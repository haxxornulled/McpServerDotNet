using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IAuthorizationService
    {
        ValueTask<Fin<bool>> IsAuthorizedAsync(string userId, string permission, CancellationToken ct);
    }
}