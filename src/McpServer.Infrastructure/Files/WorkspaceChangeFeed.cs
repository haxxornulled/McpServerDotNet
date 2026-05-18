using System.Collections.Concurrent;
using McpServer.Application.Abstractions.Files;

namespace McpServer.Infrastructure.Files;

public sealed class WorkspaceChangeFeed : IWorkspaceChangeFeed
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<WorkspaceChangeEntry> _entries = new();
    private readonly object _trimSync = new();
    public event EventHandler<WorkspaceChangeEntry>? Changed;

    public void RecordChange(string operation, string path, string? details = null, string source = "server")
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("Operation is required.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var entry = new WorkspaceChangeEntry(operation, Path.GetFullPath(path), DateTimeOffset.UtcNow, source, details);
        _entries.Enqueue(entry);
        Changed?.Invoke(this, entry);

        lock (_trimSync)
        {
            while (_entries.Count > MaxEntries && _entries.TryDequeue(out _))
            {
            }
        }
    }

    public IReadOnlyList<WorkspaceChangeEntry> GetRecentChanges(int maxEntries = 100)
    {
        if (maxEntries <= 0)
        {
            return Array.Empty<WorkspaceChangeEntry>();
        }

        return _entries
            .Reverse()
            .Take(maxEntries)
            .Reverse()
            .ToArray();
    }
}
