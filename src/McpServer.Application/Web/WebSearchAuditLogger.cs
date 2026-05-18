

namespace McpServer.Application.WebSearch
{
    // Example: Audit logger for web search
    public interface IWebSearchAuditLogger
    {
        Task LogAsync(string userId, string query, bool allowed, CancellationToken ct);
    }

    public class ConsoleWebSearchAuditLogger : IWebSearchAuditLogger
    {
        public Task LogAsync(string userId, string query, bool allowed, CancellationToken ct)
        {
            System.Console.Error.WriteLine($"[WebSearchAudit] User: {userId}, Query: '{query}', Allowed: {allowed}");
            return Task.CompletedTask;
        }
    }
}
