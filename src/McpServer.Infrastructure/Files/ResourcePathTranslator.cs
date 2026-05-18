using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Domain.Workspace;

namespace McpServer.Infrastructure.Files;

public sealed class ResourcePathTranslator : IResourcePathTranslator
{
    private readonly WorkspacePathState _state;

    public ResourcePathTranslator(string workspaceRoot)
    {
        _state = new WorkspacePathState([workspaceRoot]);
    }

    public ResourcePathTranslator(WorkspacePathState state)
    {
        _state = state;
    }

    public void SetWorkspaceRoot(string workspaceRoot) => _state.SetWorkspaceRoot(workspaceRoot);

    public void SetProjectRoot(string projectRoot) => _state.SetProjectRoot(projectRoot);

    public Fin<string> TryTranslateToLocalPath(string uri) => _state.TryTranslateToLocalPath(uri);
}
