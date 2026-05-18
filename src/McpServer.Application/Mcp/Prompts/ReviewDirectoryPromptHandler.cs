using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;

namespace McpServer.Application.Mcp.Prompts;

public sealed class ReviewDirectoryPromptHandler : IPromptHandler
{
    public string Name => "prompt.review_directory";
    public string Description => "Builds a prompt that asks the host model to review a directory resource.";

    public PromptDescriptor Describe() =>
        new(
            Name,
            "Review directory",
            Description,
            [
                new PromptArgumentDescriptor("uri", "Directory resource URI", "A dir:// resource URI to review.", true),
                new PromptArgumentDescriptor("goal", "Goal", "Optional review goal.", false)
            ]);

    public ValueTask<Fin<GetPromptResult>> GetAsync(JsonElement? arguments, CancellationToken ct)
    {
        ReviewDirectoryPromptArguments? request = null;

        if (arguments.HasValue && arguments.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            request = arguments.Value.Deserialize<ReviewDirectoryPromptArguments>();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Uri))
        {
            return ValueTask.FromResult<Fin<GetPromptResult>>(Error.New("Prompt 'prompt.review_directory' requires argument 'uri'."));
        }

        var goalClause = string.IsNullOrWhiteSpace(request.Goal)
            ? "Review the directory for concrete bugs, behavioral regressions, fragile assumptions, missing tests, and maintainability risks."
            : $"Review the directory with this goal in mind: {request.Goal}.";

        var result = new GetPromptResult(
            "Prompt for reviewing a directory resource.",
            [
                new PromptMessage(
                    "user",
                    PromptMessageContent.FromText(
                        $"Please perform a grounded code review of '{request.Uri}'. {goalClause} " +
                        "Start by calling workspace.inspect on the target path to map the tree and identify likely entry points. " +
                        "Treat workspace.inspect as reconnaissance only: do not report a finding from filenames, summaries, or guesses. " +
                        "Before reporting any finding, call fs.read_file for the specific file and verify the issue against source text. " +
                        "Every finding must cite a concrete file path and line number from lineNumberedContent or from the file content you read. " +
                        "Do not invent files, functions, line numbers, vulnerabilities, version mismatches, or test gaps. " +
                        "If you cannot prove a finding from source text, omit it. If more context is needed, call more file tools. " +
                        "If no proven issues are found, say that clearly and list the files you actually inspected. " +
                        "Return findings first, ordered by severity, with a brief recommendation for each. Keep any summary secondary."))
            ]);

        return ValueTask.FromResult<Fin<GetPromptResult>>(result);
    }
}
