using LanguageExt;

namespace McpServer.Application.Abstractions.Files;

public interface IResourcePathTranslator
{
    Fin<string> TryTranslateToLocalPath(string uri);
    void SetWorkspaceRoot(string workspaceRoot);
    void SetProjectRoot(string projectRoot);
}
