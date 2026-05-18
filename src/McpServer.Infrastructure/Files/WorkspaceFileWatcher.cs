using McpServer.Application.Abstractions.Files;
using McpServer.Domain.Workspace;

namespace McpServer.Infrastructure.Files;

public sealed class WorkspaceFileWatcher : IWorkspaceFileWatcher, IDisposable
{
    private readonly object _sync = new();
    private readonly WorkspacePathState _state;
    private readonly IWorkspaceChangeFeed _changeFeed;
    private readonly EventHandler<WorkspaceProjectRootChangedEventArgs> _projectRootChangedHandler;
    private FileSystemWatcher? _watcher;

    public WorkspaceFileWatcher(WorkspacePathState state, IWorkspaceChangeFeed changeFeed)
    {
        _state = state;
        _changeFeed = changeFeed;
        _projectRootChangedHandler = HandleProjectRootChanged;
        _state.ProjectRootChanged += _projectRootChangedHandler;

        BindWatcher(_state.ProjectRoot);
    }

    public void SetProjectRoot(string projectRoot)
    {
        _state.SetProjectRoot(projectRoot);
    }

    private void Publish(string operation, string path, string? details = null) =>
        _changeFeed.RecordChange(operation, path, details, source: "watcher");

    private void HandleProjectRootChanged(object? sender, WorkspaceProjectRootChangedEventArgs e) =>
        BindWatcher(e.ProjectRoot);

    private void BindWatcher(string projectRoot)
    {
        lock (_sync)
        {
            _watcher?.Dispose();
            _watcher = null;

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            var normalized = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(normalized))
            {
                Directory.CreateDirectory(normalized);
            }

            var watcher = new FileSystemWatcher(normalized)
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size
            };

            watcher.Changed += (_, e) => Publish("changed", e.FullPath);
            watcher.Created += (_, e) => Publish("created", e.FullPath);
            watcher.Deleted += (_, e) => Publish("deleted", e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Publish("renamed", e.FullPath, $"from={e.OldFullPath}");
            };

            _watcher = watcher;
        }
    }

    public void Dispose()
    {
        _state.ProjectRootChanged -= _projectRootChangedHandler;
        lock (_sync)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }
}
