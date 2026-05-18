using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceSelectFolderToolHandler(
    IFileSystemService fileSystemService,
    IPathPolicy pathPolicy,
    IResourcePathTranslator resourcePathTranslator,
    ILogger<WorkspaceSelectFolderToolHandler> logger,
    IWorkspaceChangeFeed? changeFeed = null,
    IWorkspaceFileWatcher? workspaceFileWatcher = null) : IToolHandler<WorkspaceSelectFolderRequest>
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

            if (!Directory.Exists(selectedPath))
            {
                return Error.New($"Directory not found: {selectedPath}");
            }

            pathPolicy.SetProjectRoot(selectedPath);
            resourcePathTranslator.SetProjectRoot(selectedPath);
            changeFeed?.RecordChange("set_project_root", selectedPath);
            workspaceFileWatcher?.SetProjectRoot(selectedPath);
            projectRootChanged = !string.Equals(selectedPath, currentProjectRoot, StringComparison.OrdinalIgnoreCase);
            browsePath = selectedPath;
            currentProjectRoot = selectedPath;
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
                folders);

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
}
