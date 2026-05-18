using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceSetRootToolHandler(
    IPathPolicy pathPolicy,
    IResourcePathTranslator resourcePathTranslator,
    ILogger<WorkspaceSetRootToolHandler> logger,
    IWorkspaceChangeFeed? changeFeed = null,
    IWorkspaceFileWatcher? workspaceFileWatcher = null) : IToolHandler<WorkspaceSetRootRequest>
{
    public string Name => "workspace.set_root";
    public string Description => "Sets the active workspace root to an existing directory inside the configured allowed roots and resets the active project folder to that root.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "Directory path to use as the workspace root. The resolved path must already be inside the configured allowed roots."
                }
            },
            required = new[] { "path" }
        });

    public IReadOnlyList<string> Validate(WorkspaceSetRootRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
        ToolRequestValidation.RequireNotRootLikePath(errors, request.Path, "path");
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(WorkspaceSetRootRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return ValueTask.FromResult<Fin<CallToolResult>>(Error.New("Path is required."));
        }

        var workspaceRoot = Path.GetFullPath(request.Path);
        if (!Directory.Exists(workspaceRoot))
        {
            return ValueTask.FromResult<Fin<CallToolResult>>(Error.New($"Directory not found: {workspaceRoot}"));
        }

        var validation = pathPolicy.NormalizeAndValidateWorkspacePath(workspaceRoot);
        if (validation.IsFail)
        {
            return ValueTask.FromResult<Fin<CallToolResult>>(PropagateFailure<CallToolResult>(validation));
        }

        var previousWorkspaceRoot = NormalizeOrNull("workspace");
        var changed = !string.Equals(previousWorkspaceRoot, workspaceRoot, StringComparison.OrdinalIgnoreCase);

        try
        {
            pathPolicy.SetWorkspaceRoot(workspaceRoot);
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult<Fin<CallToolResult>>(Error.New(ex.Message));
        }

        resourcePathTranslator.SetWorkspaceRoot(workspaceRoot);
        changeFeed?.RecordChange("set_workspace_root", workspaceRoot);
        workspaceFileWatcher?.SetProjectRoot(workspaceRoot);

        var payload = new WorkspaceSetRootResult(workspaceRoot, workspaceRoot, changed);
        var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation("Tool {ToolName} set workspace root to {WorkspaceRoot}", Name, workspaceRoot);

        return ValueTask.FromResult<Fin<CallToolResult>>(new CallToolResult(
        [
            new ContentItem("text", content)
        ],
        StructuredContent: payload));
    }

    private string? NormalizeOrNull(string path)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(path);
        return normalized.Match(
            Succ: value => value,
            Fail: _ => (string?)null);
    }

    private static Fin<T> PropagateFailure<T>(Fin<string> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);
}
