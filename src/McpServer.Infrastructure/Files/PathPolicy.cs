using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Domain.Workspace;

namespace McpServer.Infrastructure.Files;

public sealed class PathPolicy : IPathPolicy
{
    private readonly WorkspacePathState _state;

    public PathPolicy(IEnumerable<string> allowedRoots)
    {
        _state = new WorkspacePathState(allowedRoots);
    }

    public PathPolicy(WorkspacePathState state)
    {
        _state = state;
    }

    public string WorkspaceRoot => _state.WorkspaceRoot;

    public string ProjectRoot => _state.ProjectRoot;

    public IReadOnlyList<string> AllowedRoots => _state.AllowedRoots;

    public Fin<string> NormalizeAndValidateReadPath(string rawPath) => _state.NormalizeAndValidateReadPath(rawPath);

    public Fin<string> NormalizeAndValidateWritePath(string rawPath) => _state.NormalizeAndValidateWritePath(rawPath);

    public Fin<string> NormalizeAndValidateWorkspacePath(string rawPath) => _state.NormalizeAndValidateWorkspacePath(rawPath);

    public void SetAllowedRoots(IEnumerable<string> allowedRoots) => _state.SetAllowedRoots(allowedRoots);

    public void SetWorkspaceRoot(string workspaceRoot) => _state.SetWorkspaceRoot(workspaceRoot);

    public void SetProjectRoot(string projectRoot) => _state.SetProjectRoot(projectRoot);
}
