using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Domain.Workspace;
using static LanguageExt.Prelude;

namespace McpServer.Infrastructure.Files;

public sealed class DestructiveFileOperationPolicy : IDestructiveFileOperationPolicy
{
    private readonly IPathPolicy _pathPolicy;

    public DestructiveFileOperationPolicy(IPathPolicy pathPolicy)
    {
        _pathPolicy = pathPolicy;
    }

    public Fin<Unit> ValidateWrite(string normalizedPath, bool overwriteExisting)
    {
        var boundary = GetBoundaryState();
        var protectedPath = WorkspaceMutationRules.ValidateProtectedPath(normalizedPath, "write");
        if (protectedPath.IsFail)
        {
            return protectedPath;
        }

        if (overwriteExisting && boundary.IsWorkspaceBoundary(normalizedPath))
        {
            return Error.New($"Refusing to overwrite protected workspace boundary: {normalizedPath}");
        }

        return unit;
    }

    public Fin<Unit> ValidateAppend(string normalizedPath) => WorkspaceMutationRules.ValidateProtectedPath(normalizedPath, "append");

    public Fin<Unit> ValidateDelete(string normalizedPath, bool recursive, string? confirmation)
    {
        var boundary = GetBoundaryState();

        if (boundary.IsWorkspaceBoundary(normalizedPath))
        {
            return Error.New($"Refusing to delete workspace or project root: {normalizedPath}");
        }

        var protectedPath = WorkspaceMutationRules.ValidateProtectedPath(normalizedPath, "delete");
        if (protectedPath.IsFail)
        {
            return protectedPath;
        }

        if (recursive && Directory.Exists(normalizedPath))
        {
            var expectedConfirmation = $"DELETE {normalizedPath}";
            if (!string.Equals(confirmation, expectedConfirmation, StringComparison.Ordinal))
            {
                return Error.New($"Recursive delete requires confirmation exactly equal to: {expectedConfirmation}");
            }
        }

        return unit;
    }

    public Fin<Unit> ValidateMove(string normalizedSourcePath, string normalizedDestinationPath, bool overwrite)
    {
        var boundary = GetBoundaryState();

        if (boundary.IsWorkspaceBoundary(normalizedSourcePath))
        {
            return Error.New($"Refusing to move workspace or project root: {normalizedSourcePath}");
        }

        var source = WorkspaceMutationRules.ValidateProtectedPath(normalizedSourcePath, "move");
        if (source.IsFail)
        {
            return source;
        }

        var destination = WorkspaceMutationRules.ValidateProtectedPath(normalizedDestinationPath, overwrite ? "overwrite" : "create");
        if (destination.IsFail)
        {
            return destination;
        }

        return unit;
    }

    public Fin<Unit> ValidateCopy(string normalizedSourcePath, string normalizedDestinationPath, bool overwrite, bool recursive)
    {
        var boundary = GetBoundaryState();
        var destination = WorkspaceMutationRules.ValidateProtectedPath(normalizedDestinationPath, overwrite ? "overwrite" : "create");
        if (destination.IsFail)
        {
            return destination;
        }

        if (recursive && boundary.IsWorkspaceBoundary(normalizedSourcePath))
        {
            return Error.New($"Refusing to recursively copy an entire workspace or project root: {normalizedSourcePath}");
        }

        return unit;
    }

    private WorkspaceBoundaryState GetBoundaryState() =>
        new(_pathPolicy.WorkspaceRoot, _pathPolicy.ProjectRoot, _pathPolicy.AllowedRoots);
}
