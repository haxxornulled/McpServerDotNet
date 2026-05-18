using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class FileSystemServiceTests
{
    [Fact]
    public async Task WriteTextAsync_Should_Create_File_With_Empty_Content()
    {
        using var workspace = new TempWorkspace();
        var service = CreateSut(workspace.Root);

        var result = await service.WriteTextAsync(new WriteFileTextCommand("empty.txt", string.Empty), CancellationToken.None);
        var failureMessage = result.Match(
            Succ: value => value.Path,
            Fail: error => error.Message);

        Assert.True(result.IsSucc, failureMessage);

        var filePath = Path.Combine(workspace.Root, "empty.txt");
        Assert.True(File.Exists(filePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task AppendTextAsync_Should_Create_File_With_Empty_Content()
    {
        using var workspace = new TempWorkspace();
        var service = CreateSut(workspace.Root);

        var result = await service.AppendTextAsync(new AppendFileTextCommand("append-empty.txt", string.Empty), CancellationToken.None);
        var failureMessage = result.Match(
            Succ: value => value.Path,
            Fail: error => error.Message);

        Assert.True(result.IsSucc, failureMessage);

        var filePath = Path.Combine(workspace.Root, "append-empty.txt");
        Assert.True(File.Exists(filePath));
        Assert.Equal(string.Empty, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task MovePathAsync_Should_Fail_When_Source_And_Destination_Are_Same_Path()
    {
        using var workspace = new TempWorkspace();
        var service = CreateSut(workspace.Root);

        var result = await service.MovePathAsync(new MovePathCommand("same.txt", "same.txt", Overwrite: true), CancellationToken.None);

        Assert.True(result.IsFail);
        var message = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected move to fail."),
            Fail: error => error.Message);
        Assert.Contains("Source and destination paths are the same", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyPathAsync_Should_Fail_When_Copying_Directory_Without_Recursive()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "source"));
        File.WriteAllText(Path.Combine(workspace.Root, "source", "file.txt"), "data");

        var service = CreateSut(workspace.Root);

        var result = await service.CopyPathAsync(new CopyPathCommand("source", "copy", Overwrite: false, Recursive: false), CancellationToken.None);

        Assert.True(result.IsFail);
        var message = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected copy to fail."),
            Fail: error => error.Message);
        Assert.Contains("Directory copy requires recursive=true", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletePathAsync_Should_Fail_For_Missing_Path()
    {
        using var workspace = new TempWorkspace();
        var service = CreateSut(workspace.Root);

        var result = await service.DeletePathAsync(new DeletePathCommand("missing.txt"), CancellationToken.None);

        Assert.True(result.IsFail);
        var message = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected delete to fail."),
            Fail: error => error.Message);
        Assert.Contains("Path not found", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDirectoryAsync_Should_Return_Mixed_File_And_Directory_Entries()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "nested"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "alpha.txt"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "nested", "bravo.txt"), "bravo");

        var service = CreateSut(workspace.Root);

        var result = await service.ListDirectoryAsync(new ListDirectoryCommand("."), CancellationToken.None);
        var failureMessage = result.Match(
            Succ: value => value.Path,
            Fail: error => error.Message);

        Assert.True(result.IsSucc, failureMessage);

        var listing = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(2, listing.Entries.Count);
        Assert.Contains(listing.Entries, entry => entry.Name == "alpha.txt" && !entry.IsDirectory);
        Assert.Contains(listing.Entries, entry => entry.Name == "nested" && entry.IsDirectory);
    }

    [Fact]
    public async Task DeletePathAsync_Should_Fail_For_Workspace_Root()
    {
        using var workspace = new TempWorkspace();
        var service = CreateSut(workspace.Root);

        var result = await service.DeletePathAsync(new DeletePathCommand("project", Recursive: true), CancellationToken.None);

        Assert.True(result.IsFail);
        var message = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected delete to fail."),
            Fail: error => error.Message);
        Assert.Contains("Refusing to delete workspace or project root", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletePathAsync_Should_Require_Confirmation_For_Recursive_Delete()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "folder"));
        var service = CreateSut(workspace.Root);

        var result = await service.DeletePathAsync(new DeletePathCommand("folder", Recursive: true), CancellationToken.None);

        Assert.True(result.IsFail);
        var message = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected delete to fail."),
            Fail: error => error.Message);
        Assert.Contains("Recursive delete requires confirmation", message, StringComparison.Ordinal);
    }

    private static FileSystemService CreateSut(string workspaceRoot)
    {
        var pathPolicy = new PathPolicy([workspaceRoot]);
        var lockProvider = Substitute.For<IFileMutationLockProvider>();
        lockProvider.AcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult<IAsyncDisposable>(new NoopAsyncDisposable()));
        lockProvider.AcquireManyAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult<IAsyncDisposable>(new NoopAsyncDisposable()));

        var logger = Substitute.For<ILogger<FileSystemService>>();
        return new FileSystemService(pathPolicy, lockProvider, new DestructiveFileOperationPolicy(pathPolicy), logger);
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-filesystem-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
