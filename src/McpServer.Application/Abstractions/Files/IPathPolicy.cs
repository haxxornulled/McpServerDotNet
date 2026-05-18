using LanguageExt;

namespace McpServer.Application.Abstractions.Files;

public interface IPathPolicy
{
    string WorkspaceRoot { get; }
    string ProjectRoot { get; }
    IReadOnlyList<string> AllowedRoots { get; }

    Fin<string> NormalizeAndValidateReadPath(string rawPath);
    Fin<string> NormalizeAndValidateWritePath(string rawPath);
    Fin<string> NormalizeAndValidateWorkspacePath(string rawPath);
    void SetAllowedRoots(IEnumerable<string> allowedRoots);
    void SetWorkspaceRoot(string workspaceRoot);
    void SetProjectRoot(string projectRoot);
}
