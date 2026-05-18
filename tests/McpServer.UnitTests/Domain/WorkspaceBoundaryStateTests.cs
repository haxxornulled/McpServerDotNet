using McpServer.Domain.Workspace;
using Xunit;

namespace McpServer.UnitTests.Domain;

public sealed class WorkspaceBoundaryStateTests
{
    [Fact]
    public void IsWorkspaceBoundary_Should_Recognize_Exact_Workspace_Project_And_Allowed_Roots()
    {
        using var workspace = new TempWorkspace("workspace-root");
        using var project = new TempWorkspace("project-root");
        using var allowed = new TempWorkspace("allowed-root");

        var state = new WorkspaceBoundaryState(
            workspace.Root,
            project.Root,
            [workspace.Root, allowed.Root]);

        Assert.True(state.IsWorkspaceBoundary(workspace.Root));
        Assert.True(state.IsWorkspaceBoundary(project.Root));
        Assert.True(state.IsWorkspaceBoundary(allowed.Root));
        Assert.False(state.IsWorkspaceBoundary(Path.Combine(workspace.Root, "nested")));
    }

    [Fact]
    public void IsWithinAllowedRoots_Should_Recognize_Subpaths_Inside_Allowed_Roots()
    {
        using var workspace = new TempWorkspace("workspace-root");

        var state = new WorkspaceBoundaryState(
            workspace.Root,
            workspace.Root,
            [workspace.Root]);

        Assert.True(state.IsWithinAllowedRoots(workspace.Root));
        Assert.True(state.IsWithinAllowedRoots(Path.Combine(workspace.Root, "nested", "file.txt")));
        Assert.False(state.IsWithinAllowedRoots(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "outside.txt")));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace(string prefix)
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-domain-tests", prefix, Guid.NewGuid().ToString("N")));
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
