using System.Text;
using McpServer.Application.Abstractions.Files;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Activities;

public sealed class ActivityContextBuilder(
    IPathPolicy pathPolicy,
    ILogger<ActivityContextBuilder> logger) : IActivityContextBuilder
{
    private static readonly string[] AlwaysRelevantFiles =
    [
        "README.md",
        "AGENTS.md",
        "Directory.Build.props",
        "Directory.Packages.props",
        "McpServer.slnx",
        "docs/backlog.md"
    ];

    private static readonly string[] ToolingRelevantFiles =
    [
        "src/McpServer.Protocol/Routing/ToolCallRouter.cs",
        "src/McpServer.Application/Abstractions/Mcp/IToolHandler.cs",
        "src/McpServer.Host/DependencyInjection/AutofacRootModule.cs",
        "src/McpServer.Application/Mcp/Tools/WorkspaceStatusToolHandler.cs",
        "src/McpServer.Application/Mcp/Tools/WorkspaceSetRootToolHandler.cs",
        "src/McpServer.Application/Mcp/Tools/WorkspaceSelectFolderToolHandler.cs",
        "src/McpServer.Infrastructure/Files/PathPolicy.cs"
    ];

    private static readonly string[] ReviewSkillFiles =
    [
        ".ai/reviewers/deep-code-review.md"
    ];

    public ValueTask<ActivityContextPacket> BuildAsync(
        ActivityRoutingResult routing,
        ActivityProfileDto profile,
        string userRequest,
        int? maxContextBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);

        var budget = Math.Clamp(maxContextBytes.GetValueOrDefault(profile.MaxContextBytes), 8_000, 250_000);
        var includedFiles = new List<string>();
        var builder = new StringBuilder(capacity: Math.Min(budget, 64_000));
        var truncated = false;

        AppendSection(builder, "Activity", ActivityProfileRegistry.ToSnakeCase(routing.Activity));
        AppendSection(builder, "User Request", userRequest.Trim());
        AppendSection(builder, "Workspace", $"workspaceRoot: {pathPolicy.WorkspaceRoot}{Environment.NewLine}projectRoot: {pathPolicy.ProjectRoot}{Environment.NewLine}allowedRoots:{Environment.NewLine}{string.Join(Environment.NewLine, pathPolicy.AllowedRoots.Select(root => $"- {root}"))}");
        AppendSection(builder, "Routing", $"schemaName: {routing.SchemaName}{Environment.NewLine}confidence: {routing.Confidence:0.00}{Environment.NewLine}reason: {routing.Reason}");
        AppendSection(builder, "Collection Plan", BuildCollectionPlan(profile));

        foreach (var relativePath in CandidateFilesFor(routing.Activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (builder.Length >= budget)
            {
                truncated = true;
                break;
            }

            var fullPath = Path.Combine(pathPolicy.WorkspaceRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            var remaining = budget - builder.Length;
            if (remaining <= 256)
            {
                truncated = true;
                break;
            }

            try
            {
                var content = ReadFileBounded(fullPath, Math.Min(remaining, 16_000), out var fileTruncated);
                builder.AppendLine();
                builder.AppendLine("## File");
                builder.AppendLine(relativePath);
                builder.AppendLine();
                builder.AppendLine("```text");
                builder.AppendLine(content);
                builder.AppendLine("```");

                if (fileTruncated)
                {
                    builder.AppendLine("<!-- file truncated for context budget -->");
                    truncated = true;
                }

                includedFiles.Add(relativePath);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not include activity context file {Path}", fullPath);
            }
        }

        var markdown = builder.ToString();
        if (markdown.Length > budget)
        {
            markdown = markdown[..budget] + Environment.NewLine + "<!-- context truncated -->";
            truncated = true;
        }

        return ValueTask.FromResult(new ActivityContextPacket(
            routing.Activity,
            userRequest.Trim(),
            pathPolicy.WorkspaceRoot,
            pathPolicy.ProjectRoot,
            includedFiles,
            markdown,
            Encoding.UTF8.GetByteCount(markdown),
            truncated));
    }

    private static IEnumerable<string> CandidateFilesFor(ActivityKind activity)
    {
        foreach (var file in AlwaysRelevantFiles)
        {
            yield return file;
        }

        if (activity is ActivityKind.DeepCodeReview or ActivityKind.SecurityReview or ActivityKind.ArchitectureReview)
        {
            foreach (var file in ReviewSkillFiles)
            {
                yield return file;
            }
        }

        if (activity is ActivityKind.DeepCodeReview or ActivityKind.CodePatch or ActivityKind.ImplementationPlan or ActivityKind.WorkspaceSetup or ActivityKind.Diagnostic or ActivityKind.SecurityReview or ActivityKind.ArchitectureReview)
        {
            foreach (var file in ToolingRelevantFiles)
            {
                yield return file;
            }
        }
    }

    private static string BuildCollectionPlan(ActivityProfileDto profile)
    {
        var lines = new List<string>
        {
            $"structuredOutput: {profile.UseStructuredOutput}",
            $"schemaName: {profile.SchemaName}",
            $"needsWorkspaceStatus: {profile.NeedsWorkspaceStatus}",
            $"needsGitStatus: {profile.NeedsGitStatus}",
            $"needsGitDiff: {profile.NeedsGitDiff}",
            $"needsBuildOutput: {profile.NeedsBuildOutput}",
            $"needsTestOutput: {profile.NeedsTestOutput}",
            $"allowsShellExecution: {profile.AllowsShellExecution}",
            $"maxContextBytes: {profile.MaxContextBytes}"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendSection(StringBuilder builder, string title, string body)
    {
        builder.AppendLine($"# {title}");
        builder.AppendLine();
        builder.AppendLine(body);
        builder.AppendLine();
    }

    private static string ReadFileBounded(string path, int maxChars, out bool truncated)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        var buffer = new char[maxChars + 1];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        truncated = read > maxChars;
        return new string(buffer, 0, Math.Min(read, maxChars));
    }
}
