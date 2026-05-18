using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISecretService
    {
        ValueTask<Fin<string>> GetSecretAsync(string key, CancellationToken ct);
        ValueTask<Fin<Unit>> SetSecretAsync(string key, string value, CancellationToken ct);
    }
}