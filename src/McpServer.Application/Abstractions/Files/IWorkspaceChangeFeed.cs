namespace McpServer.Application.Abstractions.Files;

public interface IWorkspaceChangeFeed
{
    event EventHandler<WorkspaceChangeEntry>? Changed;
    void RecordChange(string operation, string path, string? details = null, string source = "server");
    IReadOnlyList<WorkspaceChangeEntry> GetRecentChanges(int maxEntries = 100);
}

public sealed record WorkspaceChangeEntry(
    string Operation,
    string Path,
    DateTimeOffset Timestamp,
    string Source,
    string? Details = null);
