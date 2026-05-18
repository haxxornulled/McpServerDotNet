using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IAuthorizationProvider
    {
        ValueTask<Fin<bool>> IsAuthorizedAsync(string userId, string permission, CancellationToken ct);
    }
}