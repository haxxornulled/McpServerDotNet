using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Files.Results;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceOpenToolHandler(
    IWorkspaceMutationService workspaceMutationService,
    ILogger<WorkspaceOpenToolHandler> logger,
    IPathPolicy pathPolicy) : IToolHandler<WorkspaceOpenRequest>
{
    public string Name => "workspace.open";
    public string Description => "Opens an existing folder as the active workspace root, similar to VS Code Open Folder. The folder must be inside configured allowed roots and project root is reset to the workspace root.";

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
                    minLength = 1,
                    description = "Existing directory to open as the workspace root. Must be inside configured allowed roots."
                }
            },
            required = new[] { "path" }
        });

    public IReadOnlyList<string> Validate(WorkspaceOpenRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
        ToolRequestValidation.RequireNotRootLikePath(errors, request.Path, "path");
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(WorkspaceOpenRequest request, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateWorkspacePath(request.Path);
        if (normalized.IsFail)
        {
            return ValueTask.FromResult(normalized.Match<Fin<CallToolResult>>(Succ: _ => throw new InvalidOperationException("Expected workspace path validation failure."), Fail: error => error));
        }

        var workspaceRoot = normalized.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var transition = workspaceMutationService.OpenWorkspace(workspaceRoot);
        if (transition.IsFail)
        {
            return ValueTask.FromResult(PropagateFailure<CallToolResult>(transition));
        }

        var payload = transition.Match(
            Succ: value => new WorkspaceOpenResult(value.WorkspaceRoot, value.ProjectRoot, value.Changed, pathPolicy.AllowedRoots),
            Fail: error => throw new InvalidOperationException(error.Message));
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        logger.LogInformation("Tool {ToolName} opened workspace {WorkspaceRoot}", Name, workspaceRoot);

        return ValueTask.FromResult<Fin<CallToolResult>>(new CallToolResult([new ContentItem("text", json)], StructuredContent: payload));
    }

    private static Fin<T> PropagateFailure<T>(Fin<WorkspaceTransitionResult> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);
}
