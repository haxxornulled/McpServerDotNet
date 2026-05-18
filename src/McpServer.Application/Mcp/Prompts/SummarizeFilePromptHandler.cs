using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;

namespace McpServer.Application.Mcp.Prompts;

public sealed class SummarizeFilePromptHandler : IPromptHandler
{
    public string Name => "prompt.summarize_file";
    public string Description => "Builds a prompt that asks the host model to summarize a file resource.";

    public PromptDescriptor Describe() =>
        new(
            Name,
            "Summarize file",
            Description,
            [
                new PromptArgumentDescriptor("uri", "File resource URI", "A file:// resource URI to summarize.", true),
                new PromptArgumentDescriptor("focus", "Focus", "Optional summary focus.", false)
            ]);

    public ValueTask<Fin<GetPromptResult>> GetAsync(JsonElement? arguments, CancellationToken ct)
    {
        SummarizeFilePromptArguments? request = null;

        if (arguments.HasValue && arguments.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            request = arguments.Value.Deserialize<SummarizeFilePromptArguments>();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Uri))
        {
            return ValueTask.FromResult<Fin<GetPromptResult>>(Error.New("Prompt 'prompt.summarize_file' requires argument 'uri'."));
        }

        var focusClause = string.IsNullOrWhiteSpace(request.Focus)
            ? "Provide a concise but useful summary."
            : $"Focus especially on: {request.Focus}.";

        var result = new GetPromptResult(
            "Prompt for summarizing a file resource.",
            [
                new PromptMessage(
                    "user",
                    PromptMessageContent.FromText(
                        $"Please read the resource at '{request.Uri}' and summarize it. {focusClause} Include structure, notable findings, risks, and follow-up suggestions if relevant."))
            ]);

        return ValueTask.FromResult<Fin<GetPromptResult>>(result);
    }
}
