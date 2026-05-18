using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISmsService
    {
        ValueTask<Fin<Unit>> SendSmsAsync(string to, string message, CancellationToken ct);
    }
}