using LanguageExt;

namespace McpServer.Application.Abstractions.Files;

public interface IDestructiveFileOperationPolicy
{
    Fin<Unit> ValidateWrite(string normalizedPath, bool overwriteExisting);
    Fin<Unit> ValidateAppend(string normalizedPath);
    Fin<Unit> ValidateDelete(string normalizedPath, bool recursive, string? confirmation);
    Fin<Unit> ValidateMove(string normalizedSourcePath, string normalizedDestinationPath, bool overwrite);
    Fin<Unit> ValidateCopy(string normalizedSourcePath, string normalizedDestinationPath, bool overwrite, bool recursive);
}
