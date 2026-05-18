using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IChatProvider
    {
        ValueTask<Fin<Unit>> SendMessageAsync(string userId, string message, CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<string>>> GetMessagesAsync(string userId, CancellationToken ct);
    }
}