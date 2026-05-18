using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISmsSenderProvider
    {
        ValueTask<Fin<Unit>> SendSmsAsync(string to, string message, CancellationToken ct);
    }
}