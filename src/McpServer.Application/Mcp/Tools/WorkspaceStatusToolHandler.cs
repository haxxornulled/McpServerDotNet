using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceStatusToolHandler(
    IPathPolicy pathPolicy,
    ILogger<WorkspaceStatusToolHandler> logger) : IToolHandler<WorkspaceStatusRequest>
{
    public string Name => "workspace.status";

    public string Description => "Returns the active MCP workspace root, active project root, and configured allowed roots.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new { }
        });

    public ValueTask<Fin<CallToolResult>> Handle(WorkspaceStatusRequest request, CancellationToken ct)
    {
        var payload = new WorkspaceStatusResult(
            pathPolicy.WorkspaceRoot,
            pathPolicy.ProjectRoot,
            pathPolicy.AllowedRoots);

        var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation(
            "Tool {ToolName} returned workspace status. WorkspaceRoot={WorkspaceRoot} ProjectRoot={ProjectRoot} AllowedRootCount={AllowedRootCount}",
            Name,
            payload.WorkspaceRoot,
            payload.ProjectRoot,
            payload.AllowedRoots.Count);

        return ValueTask.FromResult<Fin<CallToolResult>>(new CallToolResult(
        [
            new ContentItem("text", content)
        ],
        StructuredContent: payload));
    }
}
