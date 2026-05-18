using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface INotificationProvider
    {
        ValueTask<Fin<Unit>> SendNotificationAsync(string userId, string title, string message, CancellationToken ct);
    }
}