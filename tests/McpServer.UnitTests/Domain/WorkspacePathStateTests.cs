using McpServer.Domain.Workspace;
using Xunit;

namespace McpServer.UnitTests.Domain;

public sealed class WorkspacePathStateTests
{
    [Fact]
    public void NormalizeAndValidateWritePath_Should_Map_Relative_Path_Into_Project_Root()
    {
        using var workspace = new TempWorkspace();
        var state = new WorkspacePathState([workspace.Root]);

        var result = state.NormalizeAndValidateWritePath("notes.txt");

        Assert.True(result.IsSucc, result.Match(
            Succ: value => value,
            Fail: error => error.Message));
        Assert.Equal(
            Path.Combine(workspace.Root, "notes.txt"),
            result.Match(
                Succ: value => value,
                Fail: error => throw new InvalidOperationException(error.Message)));
    }

    [Fact]
    public void NormalizeAndValidateWorkspacePath_Should_Map_Relative_Path_Into_Workspace_Root()
    {
        using var workspace = new TempWorkspace();
        var state = new WorkspacePathState([workspace.Root]);

        var result = state.NormalizeAndValidateWorkspacePath("nested/file.txt");

        Assert.True(result.IsSucc, result.Match(
            Succ: value => value,
            Fail: error => error.Message));
        Assert.Equal(
            Path.Combine(workspace.Root, "nested", "file.txt"),
            result.Match(
                Succ: value => value,
                Fail: error => throw new InvalidOperationException(error.Message)));
    }

    [Fact]
    public void TryTranslateToLocalPath_Should_Map_Workspace_And_Project_Uris()
    {
        using var workspace = new TempWorkspace();
        var state = new WorkspacePathState([workspace.Root]);
        var project = Path.Combine(workspace.Root, "project");
        Directory.CreateDirectory(project);
        state.SetProjectRoot(project);

        var workspaceResult = state.TryTranslateToLocalPath("file:///workspace/folder/test.txt");
        var projectResult = state.TryTranslateToLocalPath("dir:///project/folder");

        Assert.True(workspaceResult.IsSucc, workspaceResult.Match(
            Succ: value => value,
            Fail: error => error.Message));
        Assert.True(projectResult.IsSucc, projectResult.Match(
            Succ: value => value,
            Fail: error => error.Message));
        Assert.Equal(
            Path.Combine(workspace.Root, "folder", "test.txt"),
            workspaceResult.Match(
                Succ: value => value,
                Fail: error => throw new InvalidOperationException(error.Message)));
        Assert.Equal(
            Path.Combine(project, "folder"),
            projectResult.Match(
                Succ: value => value,
                Fail: error => throw new InvalidOperationException(error.Message)));
    }

    [Fact]
    public void SetWorkspaceRoot_Should_Reject_Root_Outside_Allowed_Roots()
    {
        using var workspace = new TempWorkspace();
        using var outside = new TempWorkspace();
        var state = new WorkspacePathState([workspace.Root]);

        var ex = Assert.Throws<InvalidOperationException>(() => state.SetWorkspaceRoot(outside.Root));

        Assert.Contains("outside the allowed roots", ex.Message, StringComparison.Ordinal);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-domain-tests", Guid.NewGuid().ToString("N")));
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
