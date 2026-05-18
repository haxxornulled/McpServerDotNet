using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WorkspaceInspectToolHandler(
    IPathPolicy pathPolicy,
    ILogger<WorkspaceInspectToolHandler> logger) : IToolHandler<WorkspaceInspectRequest>
{
    private static readonly string[] IgnoredDirectories =
    [
        ".git",
        ".vs",
        "bin",
        "dist",
        "node_modules",
        "obj"
    ];

    private static readonly string[] PreferredFileNames =
    [
        "README.md",
        "CHANGELOG.md",
        "PF_Framework.toc",
        ".luacheckrc",
        "stylua.toml",
        "PFramework.sln"
    ];

    private static readonly string[] PreferredExtensions =
    [
        ".toc",
        ".lua",
        ".md",
        ".json",
        ".toml",
        ".xml",
        ".csproj",
        ".sln"
    ];

    public string Name => "workspace.inspect";
    public string Description => "Returns a bounded, review-ready snapshot of the active workspace: recursive tree entries plus contents of likely entry files. Use this first when the user asks for a code review without naming files.";

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
                    description = "Workspace-relative path to inspect. Defaults to workspace."
                },
                maxDepth = new
                {
                    type = "integer",
                    @default = 4,
                    minimum = 1,
                    maximum = 8
                },
                maxFiles = new
                {
                    type = "integer",
                    @default = 24,
                    minimum = 1,
                    maximum = 500
                },
                maxFileBytes = new
                {
                    type = "integer",
                    @default = 12000,
                    minimum = 512,
                    maximum = 50000
                },
                maxTotalFileBytes = new
                {
                    type = "integer",
                    description = "Maximum total source-content bytes to include across all returned files. Keeps review snapshots small enough for local models; use fs.read_file for follow-up deep reads.",
                    @default = 32768,
                    minimum = 4096,
                    maximum = 250000
                }
            }
        });

    public IReadOnlyList<string> Validate(WorkspaceInspectRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
        ToolRequestValidation.RequireRange(errors, request.MaxDepth, "maxDepth", 1, 12);
        ToolRequestValidation.RequireRange(errors, request.MaxFiles, "maxFiles", 1, 500);
        ToolRequestValidation.RequireRange(errors, request.MaxFileBytes, "maxFileBytes", 512, 262144);
        ToolRequestValidation.RequireRange(errors, request.MaxTotalFileBytes, "maxTotalFileBytes", 4096, 1048576);
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(WorkspaceInspectRequest request, CancellationToken ct)
    {
        var normalized = pathPolicy.NormalizeAndValidateReadPath(string.IsNullOrWhiteSpace(request.Path) ? "workspace" : request.Path);
        if (normalized.IsFail)
        {
            return normalized.Match<Fin<CallToolResult>>(
                Succ: _ => throw new InvalidOperationException("Expected workspace inspection path validation to fail."),
                Fail: error => error);
        }

        var root = normalized.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        if (!Directory.Exists(root))
        {
            return Error.New($"Directory not found: {root}");
        }

        var maxDepth = Math.Clamp(request.MaxDepth, 1, 8);
        var maxFiles = Math.Clamp(request.MaxFiles, 1, 500);
        var maxFileBytes = Math.Clamp(request.MaxFileBytes, 512, 50000);
        var maxTotalFileBytes = Math.Clamp(request.MaxTotalFileBytes, 4096, 250000);

        var entries = new List<WorkspaceInspectEntryDto>();
        var candidateFiles = new List<string>();
        var truncated = false;

        Walk(root, root, depth: 0);

        var selectedFiles = candidateFiles
            .OrderByDescending(IsPreferredName)
            .ThenBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToArray();

        var files = new List<WorkspaceInspectFileDto>();
        var remainingFileBytes = maxTotalFileBytes;
        foreach (var file in selectedFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (remainingFileBytes <= 0)
            {
                truncated = true;
                break;
            }

            var perFileBudget = Math.Min(maxFileBytes, remainingFileBytes);
            var inspectedFile = await ReadFileAsync(root, file, perFileBudget, ct).ConfigureAwait(false);
            files.Add(inspectedFile);
            remainingFileBytes -= Encoding.UTF8.GetByteCount(inspectedFile.Content);

            if (inspectedFile.Truncated)
            {
                truncated = true;
            }
        }

        var result = new WorkspaceInspectResult(root, entries, files, truncated || candidateFiles.Count > selectedFiles.Length);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation("Tool {ToolName} inspected {Root}", Name, root);

        return new CallToolResult(
        [
            new ContentItem("text", json)
        ],
        StructuredContent: result);

        void Walk(string baseRoot, string directory, int depth)
        {
            ct.ThrowIfCancellationRequested();

            if (depth > maxDepth)
            {
                truncated = true;
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();

                var isDirectory = Directory.Exists(entry);
                var relativePath = Path.GetRelativePath(baseRoot, entry);

                if (isDirectory && ShouldIgnoreDirectory(Path.GetFileName(entry)))
                {
                    continue;
                }

                entries.Add(new WorkspaceInspectEntryDto(relativePath, isDirectory));

                if (isDirectory)
                {
                    Walk(baseRoot, entry, depth + 1);
                }
                else if (ShouldReadFile(entry))
                {
                    candidateFiles.Add(entry);
                }
            }
        }
    }

    private static bool ShouldIgnoreDirectory(string name) =>
        IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static bool ShouldReadFile(string path)
    {
        var fileName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        return PreferredFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase) ||
            PreferredExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPreferredName(string path) =>
        PreferredFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static async Task<WorkspaceInspectFileDto> ReadFileAsync(string root, string path, int maxBytes, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var length = Math.Min(stream.Length, maxBytes);
        var buffer = new byte[length];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
        var content = Encoding.UTF8.GetString(buffer.AsSpan(0, read));
        return new WorkspaceInspectFileDto(
            Path.GetRelativePath(root, path),
            content,
            AddLineNumbers(content),
            stream.Length > maxBytes);
    }

    private static string AddLineNumbers(string content)
    {
        using var reader = new StringReader(content);
        var builder = new StringBuilder();
        var lineNumber = 1;

        while (reader.ReadLine() is { } line)
        {
            builder
                .Append(lineNumber.ToString().PadLeft(4))
                .Append(": ")
                .AppendLine(line);
            lineNumber++;
        }

        return builder.ToString();
    }
}
