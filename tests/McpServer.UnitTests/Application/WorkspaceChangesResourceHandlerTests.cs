using McpServer.Application.Abstractions.Files;
using McpServer.Application.Mcp.Resources;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceChangesResourceHandlerTests
{
    [Fact]
    public async Task ReadAsync_Should_Return_Recent_Project_Changes_Only()
    {
        using var workspace = new TempWorkspace();
        var project = Path.Combine(workspace.Root, "apps");
        Directory.CreateDirectory(project);

        var pathPolicy = new PathPolicy([workspace.Root]);
        pathPolicy.SetProjectRoot(project);

        var feed = new WorkspaceChangeFeed();
        feed.RecordChange("write", Path.Combine(project, "note.txt"), "bytes=5");
        feed.RecordChange("delete", Path.Combine(workspace.Root, "outside.txt"));

        var handler = new WorkspaceChangesResourceHandler(
            pathPolicy,
            feed,
            Substitute.For<ILogger<WorkspaceChangesResourceHandler>>());

        var result = await handler.ReadAsync("changes:///project", CancellationToken.None);

        Assert.True(result.IsSucc);
        var resource = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var content = Assert.Single(resource.Contents);
        Assert.Equal("application/json", content.MimeType);
        Assert.Contains("\"change_count\": 1", content.Text);
        Assert.Contains("\"operation\": \"write\"", content.Text);
        Assert.Contains("\"path\": \"note.txt\"", content.Text);
        Assert.DoesNotContain("outside.txt", content.Text, StringComparison.Ordinal);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; }

        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-changes-tests", Guid.NewGuid().ToString("N"));
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
