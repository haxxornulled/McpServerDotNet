using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IAuthenticationService
    {
        ValueTask<Fin<string>> AuthenticateAsync(string username, string password, CancellationToken ct);
        ValueTask<Fin<Unit>> ValidateTokenAsync(string token, CancellationToken ct);
    }
}