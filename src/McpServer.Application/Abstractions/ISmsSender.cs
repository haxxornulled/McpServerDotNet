using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISmsSender
    {
        ValueTask<Fin<Unit>> SendSmsAsync(string to, string message, CancellationToken ct);
    }
}