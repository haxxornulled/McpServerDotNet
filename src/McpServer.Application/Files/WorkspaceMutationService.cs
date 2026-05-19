using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Domain.Workspace;

namespace McpServer.Application.Files;

/// <summary>
/// Applies workspace and project root transitions against the shared workspace state.
/// </summary>
public sealed class WorkspaceMutationService(
    IPathPolicy pathPolicy,
    IResourcePathTranslator resourcePathTranslator,
    IWorkspaceChangeFeed? changeFeed = null) : McpServer.Domain.Workspace.IWorkspaceMutationService
{
    public Fin<WorkspaceTransitionResult> OpenWorkspace(string workspaceRoot) =>
        TransitionWorkspaceRoot(workspaceRoot, "open_workspace");

    public Fin<WorkspaceTransitionResult> SetWorkspaceRoot(string workspaceRoot) =>
        TransitionWorkspaceRoot(workspaceRoot, "set_workspace_root");

    public Fin<WorkspaceTransitionResult> SetProjectRoot(string projectRoot) =>
        TransitionProjectRoot(projectRoot);

    private Fin<WorkspaceTransitionResult> TransitionWorkspaceRoot(string workspaceRoot, string operation)
    {
        try
        {
            var normalized = NormalizeRequiredPath(workspaceRoot);
            if (!Directory.Exists(normalized))
            {
                return Error.New($"Directory not found: {normalized}");
            }

            var previous = pathPolicy.WorkspaceRoot;
            var changed = !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase);

            pathPolicy.SetWorkspaceRoot(normalized);
            resourcePathTranslator.SetWorkspaceRoot(normalized);
            changeFeed?.RecordChange(operation, normalized);

            return new WorkspaceTransitionResult(normalized, normalized, changed);
        }
        catch (Exception ex)
        {
            return Error.New(ex.Message);
        }
    }

    private Fin<WorkspaceTransitionResult> TransitionProjectRoot(string projectRoot)
    {
        try
        {
            var normalized = NormalizeRequiredPath(projectRoot);
            if (!Directory.Exists(normalized))
            {
                return Error.New($"Directory not found: {normalized}");
            }

            var previous = pathPolicy.ProjectRoot;
            var changed = !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase);

            pathPolicy.SetProjectRoot(normalized);
            resourcePathTranslator.SetProjectRoot(normalized);
            changeFeed?.RecordChange("set_project_root", normalized);

            return new WorkspaceTransitionResult(pathPolicy.WorkspaceRoot, normalized, changed);
        }
        catch (Exception ex)
        {
            return Error.New(ex.Message);
        }
    }

    private static string NormalizeRequiredPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Path is required.");
        }

        return Path.GetFullPath(path);
    }
}
