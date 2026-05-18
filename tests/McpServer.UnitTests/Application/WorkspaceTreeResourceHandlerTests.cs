using System.Text.Json;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Mcp.Resources;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceTreeResourceHandlerTests
{
    [Fact]
    public async Task ReadAsync_Should_Return_Recursive_Project_Tree_Snapshot()
    {
        using var workspace = new TempWorkspace();
        var project = Path.Combine(workspace.Root, "apps");
        Directory.CreateDirectory(Path.Combine(project, "sub"));
        await File.WriteAllTextAsync(Path.Combine(project, "readme.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(project, "sub", "notes.txt"), "notes");

        var pathPolicy = new PathPolicy([workspace.Root]);
        pathPolicy.SetProjectRoot(project);

        var handler = new WorkspaceTreeResourceHandler(
            pathPolicy,
            Substitute.For<ILogger<WorkspaceTreeResourceHandler>>());

        var result = await handler.ReadAsync("tree:///project", CancellationToken.None);

        Assert.True(result.IsSucc);
        var resource = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var content = Assert.Single(resource.Contents);
        Assert.NotNull(content.Text);
        var document = JsonDocument.Parse(content.Text);

        Assert.Equal(Path.GetFullPath(project), document.RootElement.GetProperty("scopeRoot").GetString());
        Assert.Equal("tree:///project", document.RootElement.GetProperty("uri").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("nodeCount").GetInt32());

        var root = document.RootElement.GetProperty("root");
        Assert.True(root.GetProperty("isDirectory").GetBoolean());

        var children = root.GetProperty("children").EnumerateArray().ToArray();
        Assert.Contains(children, child => child.GetProperty("name").GetString() == "readme.txt");
        Assert.Contains(children, child => child.GetProperty("name").GetString() == "sub");
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-tree-tests", Guid.NewGuid().ToString("N"));
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
