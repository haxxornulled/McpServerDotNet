using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Files.Results;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceSelectFolderToolHandler(
    IFileSystemService fileSystemService,
    IWorkspaceMutationService workspaceMutationService,
    IPathPolicy pathPolicy,
    ILogger<WorkspaceSelectFolderToolHandler> logger) : IToolHandler<WorkspaceSelectFolderRequest>
{
    public string Name => "workspace.select_folder";
    public string Description => "Browses the current project folder and sets the active project folder.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                path = new
                {
                    type = "string",
                    description = "Folder to open. Omit this to browse the current project folder."
                }
            }
        });

    public IReadOnlyList<string> Validate(WorkspaceSelectFolderRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNotRootLikePath(errors, request.Path, "path");
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(WorkspaceSelectFolderRequest request, CancellationToken ct)
    {
        var workspaceRoot = NormalizeOrThrow("workspace");
        var currentProjectRoot = NormalizeOrThrow("project");

        var projectRootChanged = false;
        var browsePath = currentProjectRoot;

        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            var selectedPathFin = pathPolicy.NormalizeAndValidateWorkspacePath(request.Path);
            if (selectedPathFin.IsFail)
            {
                return PropagateFailure(selectedPathFin);
            }

            var selectedPath = selectedPathFin.Match(
                Succ: path => path,
                Fail: error => throw new InvalidOperationException(error.Message));

            var transition = workspaceMutationService.SetProjectRoot(selectedPath);
            if (transition.IsFail)
            {
                return PropagateFailure(transition);
            }

            projectRootChanged = !string.Equals(selectedPath, currentProjectRoot, StringComparison.OrdinalIgnoreCase);
            browsePath = selectedPath;
            currentProjectRoot = transition.Match(
                Succ: value => value.ProjectRoot,
                Fail: error => throw new InvalidOperationException(error.Message));
        }

        var listing = await fileSystemService
            .ListDirectoryAsync(new ListDirectoryCommand(browsePath), ct)
            .ConfigureAwait(false);

        return listing.Map(result =>
        {
            var folders = result.Entries
                .Where(entry => entry.IsDirectory)
                .Select(entry => new WorkspaceFolderOption(
                    entry.Name,
                    Path.GetFullPath(Path.Combine(result.Path, entry.Name))))
                .ToArray();

            var payload = new WorkspaceSelectFolderResult(
                workspaceRoot,
                currentProjectRoot,
                browsePath,
                projectRootChanged,
                BuildFolderOptions(result.Entries, result.Path));

            logger.LogInformation(
                "Tool {ToolName} completed for browse path {BrowsePath} project root changed {ProjectRootChanged}",
                Name,
                browsePath,
                projectRootChanged);

            var content = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

            return new CallToolResult(
            [
                new ContentItem("text", content)
            ],
            StructuredContent: payload);
        });
    }

    private string NormalizeOrThrow(string path)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(path);
        return normalized.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
    }

    private static Fin<CallToolResult> PropagateFailure(Fin<string> failure) =>
        failure.Match<Fin<CallToolResult>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);

    private static Fin<CallToolResult> PropagateFailure(Fin<WorkspaceTransitionResult> failure) =>
        failure.Match<Fin<CallToolResult>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);

    private static WorkspaceFolderOption[] BuildFolderOptions(
        IReadOnlyList<DirectoryEntry> entries,
        string basePath)
    {
        var folders = new List<WorkspaceFolderOption>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            if (!entries[i].IsDirectory)
            {
                continue;
            }

            folders.Add(new WorkspaceFolderOption(
                entries[i].Name,
                Path.GetFullPath(Path.Combine(basePath, entries[i].Name))));
        }

        return folders.ToArray();
    }
}
