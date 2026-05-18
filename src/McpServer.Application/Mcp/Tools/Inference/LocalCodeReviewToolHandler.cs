using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Inference;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

public sealed class LocalCodeReviewToolHandler : IToolHandler<LocalCodeReviewRequest>
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LocalCodeReviewToolHandler> _logger;

    public LocalCodeReviewToolHandler(
        ILocalInferenceService inferenceService,
        ILogger<LocalCodeReviewToolHandler> logger)
    {
        _inferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "inference.local_code_review";

    public string Description => "Runs a cheap first-pass local Ollama review over provided code or diffs. Codex should verify final changes itself.";

    public JsonElement GetInputSchema()
    {
        return LocalInferenceToolSupport.CreateTextGenerationSchema("content", new
        {
            content = new { type = "string", description = "Code, diff, or selected file content to review." },
            filePath = new { type = "string", description = "Optional source path for context only." },
            instructions = new { type = "string", description = "Optional review focus." },
            model = new { type = "string", description = "Optional locally allowed Ollama model name." },
            maxOutputChars = new { type = "integer", minimum = 256, maximum = 200000 },
            timeoutSeconds = new { type = "integer", minimum = 1, maximum = 600 }
        });
    }

    public IReadOnlyList<string> Validate(LocalCodeReviewRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Content, "content");
        LocalInferenceToolSupport.ValidateOptionalGenerationControls(
            errors,
            temperature: null,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(LocalCodeReviewRequest request, CancellationToken ct)
    {
        var inferenceRequest = new LocalInferenceRequest(
            operation: "code_review",
            prompt: BuildPrompt(request),
            systemPrompt: "You are a local first-pass code reviewer. Find concrete correctness, security, concurrency, performance, and maintainability issues. Be concise and cite exact symbols or snippets when possible.",
            model: request.Model,
            temperature: 0.1d,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);

        return LocalInferenceToolSupport.CompleteAsync(Name, _inferenceService, _logger, inferenceRequest, ct);
    }

    private static string BuildPrompt(LocalCodeReviewRequest request)
    {
        var path = string.IsNullOrWhiteSpace(request.FilePath) ? "<unspecified>" : request.FilePath.Trim();
        var instructions = string.IsNullOrWhiteSpace(request.Instructions)
            ? "Return Critical, Warnings, Suggestions, and Looks Good sections."
            : request.Instructions.Trim();

        return $"Path: {path}\nInstructions: {instructions}\n\nContent:\n{request.Content}";
    }
}
