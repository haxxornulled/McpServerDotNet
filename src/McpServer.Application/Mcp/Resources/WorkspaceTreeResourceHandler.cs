using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Resources;

public sealed class WorkspaceTreeResourceHandler(
    IPathPolicy pathPolicy,
    ILogger<WorkspaceTreeResourceHandler> logger) : IResourceHandler
{
    public string UriScheme => "tree";
    public string Name => "tree";
    public string Description => "Returns a recursive filesystem snapshot for the active project or workspace.";

    public ResourceDescriptor Describe() =>
        new("tree", "Workspace tree snapshot", "tree:///project", Description, "application/json");

    public ValueTask<Fin<ReadResourceResult>> ReadAsync(string uri, CancellationToken ct)
    {
        try
        {
            var (scopeRoot, localRoot) = ResolveScopeRoot(uri);
            var snapshot = BuildSnapshot(uri, scopeRoot, localRoot, ct);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

            logger.LogInformation("Workspace tree snapshot read for {Uri} at {ScopeRoot}", uri, scopeRoot);
            return ValueTask.FromResult<Fin<ReadResourceResult>>(new ReadResourceResult([new ResourceContent(uri, "application/json", text: json)]));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult<Fin<ReadResourceResult>>(LanguageExt.Common.Error.New(ex.Message));
        }
    }

    private WorkspaceTreeSnapshotDto BuildSnapshot(string uri, string scopeRoot, string localRoot, CancellationToken ct)
    {
        var nodeCount = 0;
        var directoryCount = 0;
        var fileCount = 0;

        WorkspaceTreeNodeDto BuildNode(string path)
        {
            ct.ThrowIfCancellationRequested();

            var name = string.Equals(path, scopeRoot, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileName(path);

            if (string.IsNullOrWhiteSpace(name))
            {
                name = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            if (Directory.Exists(path))
            {
                directoryCount++;
                var children = Directory.EnumerateFileSystemEntries(path)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(BuildNode)
                    .ToArray();

                nodeCount++;
                return new WorkspaceTreeNodeDto(name, path, true, children);
            }

            fileCount++;
            nodeCount++;
            return new WorkspaceTreeNodeDto(name, path, false);
        }

        var root = BuildNode(localRoot);
        return new WorkspaceTreeSnapshotDto(scopeRoot, uri, root, nodeCount, directoryCount, fileCount);
    }

    private (string ScopeRoot, string LocalRoot) ResolveScopeRoot(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException($"Invalid URI: {uri}");
        }

        var localPath = parsed.LocalPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(localPath) || localPath.Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            var scope = Normalize("project");
            return (scope, scope);
        }

        if (localPath.StartsWith("project" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var scope = Normalize("project");
            var combined = Normalize(Path.Combine("project", localPath[("project".Length + 1)..]));
            return (scope, combined);
        }

        if (localPath.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            var scope = Normalize("workspace");
            return (scope, scope);
        }

        if (localPath.StartsWith("workspace" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var scope = Normalize("workspace");
            var combined = Normalize(Path.Combine("workspace", localPath[("workspace".Length + 1)..]));
            return (scope, combined);
        }

        throw new InvalidOperationException("Tree resource must be rooted under /workspace or /project.");
    }

    private string Normalize(string relativePath)
    {
        var result = pathPolicy.NormalizeAndValidateReadPath(relativePath);
        return result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
    }
}
