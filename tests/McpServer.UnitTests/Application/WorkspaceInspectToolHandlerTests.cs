using McpServer.Application.Mcp.Tools;
using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class WorkspaceInspectToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_Tree_And_Preferred_File_Contents()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "Lua"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "README.md"), "# Project");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "PF_Framework.toc"), "## Interface: 110000");
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "Lua", "Core.lua"), "print('hello')");

        var handler = new WorkspaceInspectToolHandler(
            new PathPolicy([workspace.Root]),
            Substitute.For<ILogger<WorkspaceInspectToolHandler>>());

        var result = await handler.Handle(new WorkspaceInspectRequest(), CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = Assert.IsType<WorkspaceInspectResult>(dto.StructuredContent);
        Assert.Contains(structured.Entries, entry => entry.Path == "Lua" && entry.IsDirectory);
        Assert.Contains(structured.Files, file => file.Path == "README.md" && file.Content.Contains("# Project", StringComparison.Ordinal));
        Assert.Contains(structured.Files, file => file.Path == "README.md" && file.LineNumberedContent.Contains("   1: # Project", StringComparison.Ordinal));
        Assert.Contains(structured.Files, file => file.Path == "PF_Framework.toc" && file.Content.Contains("Interface", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetInputSchema_Should_Allow_Larger_Model_Requested_File_Limits()
    {
        var handler = new WorkspaceInspectToolHandler(
            new PathPolicy([Path.GetTempPath()]),
            Substitute.For<ILogger<WorkspaceInspectToolHandler>>());

        var schema = handler.GetInputSchema();

        Assert.Equal(
            500,
            schema.GetProperty("properties")
                .GetProperty("maxFiles")
                .GetProperty("maximum")
                .GetInt32());

        Assert.Equal(
            32768,
            schema.GetProperty("properties")
                .GetProperty("maxTotalFileBytes")
                .GetProperty("default")
                .GetInt32());
    }

    [Fact]
    public async Task Handle_Should_Respect_Total_File_Content_Budget()
    {
        using var workspace = new TempWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "README.md"), new string('a', 3000));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "CHANGELOG.md"), new string('b', 3000));
        await File.WriteAllTextAsync(Path.Combine(workspace.Root, "PF_Framework.toc"), new string('c', 3000));

        var handler = new WorkspaceInspectToolHandler(
            new PathPolicy([workspace.Root]),
            Substitute.For<ILogger<WorkspaceInspectToolHandler>>());

        var result = await handler.Handle(
            new WorkspaceInspectRequest(MaxFiles: 10, MaxFileBytes: 3000, MaxTotalFileBytes: 4096),
            CancellationToken.None);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var structured = Assert.IsType<WorkspaceInspectResult>(dto.StructuredContent);
        Assert.True(structured.Truncated);
        Assert.InRange(structured.Files.Sum(file => System.Text.Encoding.UTF8.GetByteCount(file.Content)), 1, 4096);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mcpserver-workspace-inspect-tests", Guid.NewGuid().ToString("N"));

        public TempWorkspace()
        {
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
