using System.Text.Json;
using McpServer.IntegrationTests.Infrastructure;
using Xunit;

namespace McpServer.IntegrationTests.Protocol;

public sealed class McpDefaultToolCoverageIntegrationTests
{
    private static readonly string[] ExpectedDefaultTools =
    [
        "activity.context.preview",
        "activity.route",
        "activity.run",
        "activity.schemas.list",
        "fs.append_text",
        "fs.copy_path",
        "fs.create_directory",
        "fs.delete_path",
        "fs.get_metadata",
        "fs.list_directory",
        "fs.move_path",
        "fs.read_file",
        "fs.read_text",
        "fs.write_text",
        "workspace.inspect",
        "workspace.open",
        "workspace.select_folder",
        "workspace.set_root",
        "workspace.status"
    ];

    private static string HostProjectPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "McpServer.Host", "McpServer.Host.csproj"));

    [Fact]
    public async Task ToolsList_Should_Expose_All_Default_Tools_With_Valid_InputSchemas()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot);

        using var response = await session.ListToolsAsync();
        McpStdioIntegrationTestSession.AssertJsonRpcSucceeded(response, "tools/list");

        var tools = response.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .ToArray();

        var actualNames = tools
            .Select(static tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedDefaultTools.OrderBy(static name => name, StringComparer.Ordinal), actualNames);

        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString();
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.True(tool.TryGetProperty("description", out var description));
            Assert.Equal(JsonValueKind.String, description.ValueKind);
            Assert.True(tool.TryGetProperty("inputSchema", out var inputSchema), $"Tool '{name}' did not expose inputSchema.");
            Assert.Equal(JsonValueKind.Object, inputSchema.ValueKind);
            Assert.True(inputSchema.TryGetProperty("type", out var schemaType), $"Tool '{name}' inputSchema did not expose type.");
            Assert.Equal("object", schemaType.GetString());
        }
    }

    [Fact]
    public async Task Default_Tools_Should_Execute_End_To_End_Against_Isolated_Workspace()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot);

        await AssertToolSucceeds(session, "activity.schemas.list", new { });
        await AssertToolSucceeds(session, "activity.route", new { request = "Diagnose a build failure in this repository." });
        await AssertToolSucceeds(session, "activity.context.preview", new
        {
            request = "Explain the current isolated integration workspace.",
            activity = "explain",
            maxContextBytes = 4000
        });
        await AssertToolSucceeds(session, "activity.run", new
        {
            request = "Explain the current isolated integration workspace.",
            activity = "explain",
            maxContextBytes = 4000,
            runBuild = false,
            runTests = false
        });

        await AssertToolSucceeds(session, "workspace.status", new { });
        await AssertToolSucceeds(session, "workspace.open", new { path = workspaceRoot });
        await AssertToolSucceeds(session, "workspace.set_root", new { path = workspaceRoot });

        await AssertToolSucceeds(session, "fs.create_directory", new { path = "integration-tools" });
        await AssertToolSucceeds(session, "fs.write_text", new
        {
            path = "integration-tools/source.txt",
            content = "hello",
            overwrite = true
        });
        await AssertToolSucceeds(session, "fs.append_text", new
        {
            path = "integration-tools/source.txt",
            content = Environment.NewLine + "world",
            flush = true
        });
        await AssertToolSucceeds(session, "fs.read_text", new { path = "integration-tools/source.txt" });
        await AssertToolSucceeds(session, "fs.read_file", new { path = "integration-tools/source.txt" });
        await AssertToolSucceeds(session, "fs.get_metadata", new { path = "integration-tools/source.txt" });
        await AssertToolSucceeds(session, "fs.list_directory", new { path = "integration-tools" });
        await AssertToolSucceeds(session, "fs.copy_path", new
        {
            source_path = "integration-tools/source.txt",
            destination_path = "integration-tools/copy.txt",
            overwrite = true
        });
        await AssertToolSucceeds(session, "fs.move_path", new
        {
            source_path = "integration-tools/copy.txt",
            destination_path = "integration-tools/moved.txt",
            overwrite = true
        });
        await AssertToolSucceeds(session, "fs.delete_path", new
        {
            path = "integration-tools/moved.txt",
            recursive = false
        });

        await AssertToolSucceeds(session, "workspace.select_folder", new { path = "integration-tools" });
        await AssertToolSucceeds(session, "workspace.inspect", new
        {
            path = ".",
            maxDepth = 1,
            maxFiles = 8,
            maxFileBytes = 4096,
            maxTotalFileBytes = 8192
        });

        Assert.True(File.Exists(Path.Combine(workspaceRoot, "integration-tools", "source.txt")));
        Assert.False(File.Exists(Path.Combine(workspaceRoot, "integration-tools", "moved.txt")));
    }

    private static async Task AssertToolSucceeds(
        McpStdioIntegrationTestSession session,
        string toolName,
        object arguments)
    {
        using var response = await session.CallToolAsync(toolName, arguments);
        McpStdioIntegrationTestSession.AssertToolSucceeded(response, toolName);
    }

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpserver-tool-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
