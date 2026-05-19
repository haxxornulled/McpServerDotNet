using LanguageExt;
using McpServer.Application.Files.Results;

namespace McpServer.Application.Abstractions.Files;

/// <summary>
/// Coordinates workspace root and project root transitions for the active session.
/// </summary>
public interface IWorkspaceMutationService
{
    /// <summary>
    /// Opens a folder as the active workspace root and resets the project root to match.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root to activate.</param>
    /// <returns>The resulting workspace transition snapshot.</returns>
    Fin<WorkspaceTransitionResult> OpenWorkspace(string workspaceRoot);

    /// <summary>
    /// Replaces the active workspace root and resets the project root to match.
    /// </summary>
    /// <param name="workspaceRoot">The new workspace root.</param>
    /// <returns>The resulting workspace transition snapshot.</returns>
    Fin<WorkspaceTransitionResult> SetWorkspaceRoot(string workspaceRoot);

    /// <summary>
    /// Replaces the active project root while keeping it within the active workspace root.
    /// </summary>
    /// <param name="projectRoot">The new project root.</param>
    /// <returns>The resulting workspace transition snapshot.</returns>
    Fin<WorkspaceTransitionResult> SetProjectRoot(string projectRoot);
}
