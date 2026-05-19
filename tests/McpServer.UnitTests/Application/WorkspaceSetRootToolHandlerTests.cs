using McpServer.Application.Mcp.Tools;
using McpServer.Application.Files;
using McpServer.Application.Files.Results;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceSetRootToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Replace_Workspace_Root_And_Reset_Project_Root()
    {
        using var originalWorkspace = new TempWorkspace("mcpserver-workspace-set-root-original");
        var newWorkspaceRoot = Path.Combine(originalWorkspace.Root, "nested-workspace");
        Directory.CreateDirectory(Path.Combine(newWorkspaceRoot, "src"));

        var pathPolicy = new PathPolicy([originalWorkspace.Root]);
        var resourceTranslator = new ResourcePathTranslator(originalWorkspace.Root);
        var changeFeed = new WorkspaceChangeFeed();
        var workspaceMutationService = new WorkspaceMutationService(pathPolicy, resourceTranslator, changeFeed);

        var handler = new WorkspaceSetRootToolHandler(
            workspaceMutationService,
            Substitute.For<ILogger<WorkspaceSetRootToolHandler>>(),
            pathPolicy);

        var result = await handler.Handle(new WorkspaceSetRootRequest(newWorkspaceRoot), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = Assert.IsType<WorkspaceSetRootResult>(dto.StructuredContent);
        Assert.Equal(newWorkspaceRoot, structured.WorkspaceRoot);
        Assert.Equal(newWorkspaceRoot, structured.ProjectRoot);
        Assert.True(structured.Changed);

        var normalizedProjectPath = pathPolicy.NormalizeAndValidateReadPath("src");
        Assert.True(normalizedProjectPath.IsSucc);
        Assert.Equal(
            Path.Combine(newWorkspaceRoot, "src"),
            normalizedProjectPath.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message)));

        var translatedWorkspacePath = resourceTranslator.TryTranslateToLocalPath("dir:///workspace/src");
        Assert.True(translatedWorkspacePath.IsSucc);
        Assert.Equal(
            Path.Combine(newWorkspaceRoot, "src"),
            translatedWorkspacePath.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message)));

        var changes = changeFeed.GetRecentChanges();
        Assert.Single(changes);
        Assert.Equal("set_workspace_root", changes[0].Operation);
        Assert.Equal(newWorkspaceRoot, changes[0].Path);
    }

    [Fact]
    public async Task Handle_Should_Return_Error_When_New_Root_Is_Outside_Allowed_Roots()
    {
        using var workspace = new TempWorkspace("mcpserver-workspace-set-root-original");
        using var outside = new TempWorkspace("mcpserver-workspace-set-root-outside");
        var pathPolicy = new PathPolicy([workspace.Root]);

        var handler = new WorkspaceSetRootToolHandler(
            new WorkspaceMutationService(
                pathPolicy,
                new ResourcePathTranslator(workspace.Root)),
            Substitute.For<ILogger<WorkspaceSetRootToolHandler>>(),
            pathPolicy);

        var result = await handler.Handle(new WorkspaceSetRootRequest(outside.Root), CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public async Task Handle_Should_Return_Error_When_Directory_Does_Not_Exist()
    {
        using var workspace = new TempWorkspace("mcpserver-workspace-set-root-missing");
        var missing = Path.Combine(workspace.Root, "missing");
        var pathPolicy = new PathPolicy([workspace.Root]);

        var handler = new WorkspaceSetRootToolHandler(
            new WorkspaceMutationService(
                pathPolicy,
                new ResourcePathTranslator(workspace.Root)),
            Substitute.For<ILogger<WorkspaceSetRootToolHandler>>(),
            pathPolicy);

        var result = await handler.Handle(new WorkspaceSetRootRequest(missing), CancellationToken.None);

        Assert.True(result.IsFail);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace(string prefix)
        {
            Root = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
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
