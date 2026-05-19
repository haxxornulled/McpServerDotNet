using McpServer.Application.Files;
using McpServer.Application.Files.Results;
using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceMutationServiceTests
{
    [Fact]
    public void OpenWorkspace_Should_Set_Workspace_And_Project_Roots_And_Record_Change()
    {
        using var workspace = new TempWorkspace();
        var openRoot = Path.Combine(workspace.Root, "workspace-open");
        Directory.CreateDirectory(openRoot);

        var pathPolicy = new PathPolicy([workspace.Root]);
        var translator = new ResourcePathTranslator(workspace.Root);
        var feed = new WorkspaceChangeFeed();
        var service = new WorkspaceMutationService(pathPolicy, translator, feed);

        var result = service.OpenWorkspace(openRoot);

        Assert.True(result.IsSucc);
        var transition = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal(openRoot, transition.WorkspaceRoot);
        Assert.Equal(openRoot, transition.ProjectRoot);
        Assert.True(transition.Changed);
        Assert.Equal(openRoot, pathPolicy.WorkspaceRoot);
        Assert.Equal(openRoot, pathPolicy.ProjectRoot);

        var changes = feed.GetRecentChanges();
        Assert.Single(changes);
        Assert.Equal("open_workspace", changes[0].Operation);
        Assert.Equal(openRoot, changes[0].Path);
    }

    [Fact]
    public void SetProjectRoot_Should_Update_Project_Root_Without_Replacing_Workspace_Root()
    {
        using var workspace = new TempWorkspace();
        var projectRoot = Path.Combine(workspace.Root, "apps");
        Directory.CreateDirectory(projectRoot);

        var pathPolicy = new PathPolicy([workspace.Root]);
        var translator = new ResourcePathTranslator(workspace.Root);
        var feed = new WorkspaceChangeFeed();
        var service = new WorkspaceMutationService(pathPolicy, translator, feed);

        var result = service.SetProjectRoot(projectRoot);

        Assert.True(result.IsSucc);
        var transition = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal(workspace.Root, transition.WorkspaceRoot);
        Assert.Equal(projectRoot, transition.ProjectRoot);
        Assert.True(transition.Changed);
        Assert.Equal(workspace.Root, pathPolicy.WorkspaceRoot);
        Assert.Equal(projectRoot, pathPolicy.ProjectRoot);

        var changes = feed.GetRecentChanges();
        Assert.Single(changes);
        Assert.Equal("set_project_root", changes[0].Operation);
        Assert.Equal(projectRoot, changes[0].Path);
    }

    [Fact]
    public void SetWorkspaceRoot_Should_Reject_Roots_Outside_Allowed_Roots()
    {
        using var workspace = new TempWorkspace();
        using var outside = new TempWorkspace();

        var pathPolicy = new PathPolicy([workspace.Root]);
        var translator = new ResourcePathTranslator(workspace.Root);
        var service = new WorkspaceMutationService(pathPolicy, translator);

        var result = service.SetWorkspaceRoot(outside.Root);

        Assert.True(result.IsFail);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-mutation-tests", Guid.NewGuid().ToString("N"));
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
