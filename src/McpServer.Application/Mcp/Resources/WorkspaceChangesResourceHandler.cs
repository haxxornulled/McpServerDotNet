using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Resources;

public sealed class WorkspaceChangesResourceHandler(
    IPathPolicy pathPolicy,
    IWorkspaceChangeFeed changeFeed,
    ILogger<WorkspaceChangesResourceHandler> logger) : IResourceHandler
{
    public string UriScheme => "changes";
    public string Name => "changes";
    public string Description => "Lists recent filesystem mutations in the active project root.";

    public ResourceDescriptor Describe() =>
        new("changes", "Workspace change feed", "changes:///project", Description, "application/json");

    public ValueTask<Fin<ReadResourceResult>> ReadAsync(string uri, CancellationToken ct)
    {
        try
        {
            var scopeRoot = ResolveScopeRoot(uri);
            var changes = changeFeed.GetRecentChanges(100)
                .Where(change => IsUnderScope(change.Path, scopeRoot))
                .Select(change => new
                {
                    operation = change.Operation,
                    path = Path.GetRelativePath(scopeRoot, change.Path),
                    absolute_path = change.Path,
                    timestamp = change.Timestamp,
                    details = change.Details
                })
                .ToArray();

            var payload = new
            {
                scope_root = scopeRoot,
                change_count = changes.Length,
                changes
            };

            logger.LogInformation("Read workspace change feed for {ScopeRoot} with {ChangeCount} changes", scopeRoot, changes.Length);

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return ValueTask.FromResult<Fin<ReadResourceResult>>(new ReadResourceResult([new ResourceContent(uri, "application/json", text: json)]));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult<Fin<ReadResourceResult>>(LanguageExt.Common.Error.New(ex.Message));
        }
    }

    private string ResolveScopeRoot(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException($"Invalid URI: {uri}");
        }

        var path = parsed.LocalPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (path.Equals("workspace", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeRoot("workspace");
        }

        if (path.Equals("project", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(path))
        {
            return NormalizeRoot("project");
        }

        throw new InvalidOperationException("Workspace change feed only supports /workspace or /project scopes.");
    }

    private string NormalizeRoot(string path)
    {
        var result = pathPolicy.NormalizeAndValidateReadPath(path);
        return result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
    }

    private static bool IsUnderScope(string path, string scopeRoot) =>
        path.Equals(scopeRoot, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(scopeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
