using McpServer.Application.Abstractions.Files;
using McpServer.Infrastructure.Files;
using McpServer.Domain.Workspace;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class WorkspaceFileWatcherTests
{
    [Fact]
    public async Task SetProjectRoot_Should_Record_External_File_Creation_Changes()
    {
        using var workspace = new TempWorkspace();
        var feed = new WorkspaceChangeFeed();
        var state = new WorkspacePathState([workspace.Root]);
        using var watcher = new WorkspaceFileWatcher(state, feed);

        var tcs = new TaskCompletionSource<WorkspaceChangeEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        feed.Changed += (_, entry) =>
        {
            if (entry.Operation == "created" && entry.Path.EndsWith("watched.txt", StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetResult(entry);
            }
        };

        var watchedRoot = Path.Combine(workspace.Root, "watched-project");
        Directory.CreateDirectory(watchedRoot);
        state.SetProjectRoot(watchedRoot);

        var filePath = Path.Combine(watchedRoot, "watched.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, completed);

        var entry = await tcs.Task;
        Assert.Equal("created", entry.Operation);
        Assert.Equal("watcher", entry.Source);
        Assert.Equal(Path.GetFullPath(filePath), entry.Path);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-watcher-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
