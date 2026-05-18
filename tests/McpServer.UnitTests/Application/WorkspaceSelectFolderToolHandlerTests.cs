using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Mcp.Tools;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceSelectFolderToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Set_Active_Project_Folder_And_Browse_It()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "apps", "sub"));
        Directory.CreateDirectory(Path.Combine(workspace.Root, "docs"));
        File.WriteAllText(Path.Combine(workspace.Root, "apps", "readme.txt"), "hello");

        var selectionPolicy = new PathPolicy([workspace.Root]);
        var resourceTranslator = new ResourcePathTranslator(workspace.Root);
        var changeFeed = new WorkspaceChangeFeed();
        var fileSystemService = new FileSystemService(
            new PathPolicy([workspace.Root]),
            Substitute.For<IFileMutationLockProvider>(),
            Substitute.For<ILogger<FileSystemService>>());

        var handler = new WorkspaceSelectFolderToolHandler(
            fileSystemService,
            selectionPolicy,
            resourceTranslator,
            Substitute.For<ILogger<WorkspaceSelectFolderToolHandler>>(),
            changeFeed);

        var result = await handler.Handle(new WorkspaceSelectFolderRequest("apps"), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.False(dto.IsError);
        var structured = Assert.IsType<WorkspaceSelectFolderResult>(dto.StructuredContent);
        Assert.Equal(workspace.Root, structured.WorkspaceRoot);
        Assert.Equal(Path.Combine(workspace.Root, "apps"), structured.ProjectRoot);
        Assert.True(structured.ProjectRootChanged);
        Assert.Equal(Path.Combine(workspace.Root, "apps"), structured.BrowsedPath);
        Assert.Contains(structured.Folders, folder => folder.Name == "sub");
        Assert.DoesNotContain(structured.Folders, folder => folder.Name == "docs");

        var normalizedWrite = selectionPolicy.NormalizeAndValidateWritePath("note.txt");
        Assert.True(normalizedWrite.IsSucc);
        var normalizedWritePath = normalizedWrite.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace.Root, "apps", "note.txt"), normalizedWritePath);

        var projectUri = resourceTranslator.TryTranslateToLocalPath("file:///project/note.txt");
        Assert.True(projectUri.IsSucc);
        var translatedProjectPath = projectUri.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace.Root, "apps", "note.txt"), translatedProjectPath);

        var changes = changeFeed.GetRecentChanges();
        Assert.Single(changes);
        Assert.Equal("set_project_root", changes[0].Operation);
        Assert.Equal(Path.Combine(workspace.Root, "apps"), changes[0].Path);
    }

    [Fact]
    public async Task Handle_Should_Browse_Current_Project_Folder_When_No_Path_Is_Provided()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "src"));
        Directory.CreateDirectory(Path.Combine(workspace.Root, "docs"));

        var selectionPolicy = new PathPolicy([workspace.Root]);
        var resourceTranslator = new ResourcePathTranslator(workspace.Root);
        var fileSystemService = new FileSystemService(
            new PathPolicy([workspace.Root]),
            Substitute.For<IFileMutationLockProvider>(),
            Substitute.For<ILogger<FileSystemService>>());

        var handler = new WorkspaceSelectFolderToolHandler(
            fileSystemService,
            selectionPolicy,
            resourceTranslator,
            Substitute.For<ILogger<WorkspaceSelectFolderToolHandler>>());

        var result = await handler.Handle(new WorkspaceSelectFolderRequest(), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = Assert.IsType<WorkspaceSelectFolderResult>(dto.StructuredContent);
        Assert.False(structured.ProjectRootChanged);
        Assert.Equal(workspace.Root, structured.ProjectRoot);
        Assert.Equal(workspace.Root, structured.BrowsedPath);
        Assert.Contains(structured.Folders, folder => folder.Name == "src");
        Assert.Contains(structured.Folders, folder => folder.Name == "docs");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-select-tests", Guid.NewGuid().ToString("N"));
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
