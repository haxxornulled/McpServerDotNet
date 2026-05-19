namespace McpServer.Application.Files.Results;

/// <summary>
/// Describes the workspace and project roots after a workspace transition.
/// </summary>
public sealed record WorkspaceTransitionResult(
    string WorkspaceRoot,
    string ProjectRoot,
    bool Changed);
