using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using McpServer.Domain.Workspace;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceSetRootToolHandler(
    McpServer.Domain.Workspace.IWorkspaceMutationService workspaceMutationService,
    ILogger<WorkspaceSetRootToolHandler> logger,
    IPathPolicy pathPolicy) : IToolHandler<WorkspaceSetRootRequest>
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

        var validation = pathPolicy.NormalizeAndValidateWorkspacePath(request.Path);
        if (validation.IsFail)
        {
            return ValueTask.FromResult(validation.Match<Fin<CallToolResult>>(Succ: _ => throw new InvalidOperationException("Expected workspace path validation failure."), Fail: error => error));
        }

        var workspaceRoot = validation.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var transition = workspaceMutationService.SetWorkspaceRoot(workspaceRoot);
        if (transition.IsFail)
        {
            return ValueTask.FromResult(PropagateFailure<CallToolResult>(transition));
        }

        var payload = transition.Match(
            Succ: value => new WorkspaceSetRootResult(value.WorkspaceRoot, value.ProjectRoot, value.Changed),
            Fail: error => throw new InvalidOperationException(error.Message));
        var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation("Tool {ToolName} set workspace root to {WorkspaceRoot}", Name, workspaceRoot);

        return ValueTask.FromResult<Fin<CallToolResult>>(new CallToolResult(
        [
            new ContentItem("text", content)
        ],
        StructuredContent: payload));
    }

    private static Fin<T> PropagateFailure<T>(Fin<WorkspaceTransitionResult> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);
}
