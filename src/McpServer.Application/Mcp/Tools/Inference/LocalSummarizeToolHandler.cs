using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Inference;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

public sealed class LocalSummarizeToolHandler : IToolHandler<LocalSummarizeRequest>
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LocalSummarizeToolHandler> _logger;

    public LocalSummarizeToolHandler(
        ILocalInferenceService inferenceService,
        ILogger<LocalSummarizeToolHandler> logger)
    {
        _inferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "inference.local_summarize";

    public string Description => "Uses the local Ollama model for cheap summarization work before Codex spends higher-reasoning tokens.";

    public JsonElement GetInputSchema()
    {
        return LocalInferenceToolSupport.CreateTextGenerationSchema("text", new
        {
            text = new { type = "string", description = "Text to summarize." },
            focus = new { type = "string", description = "Optional focus area for the summary." },
            model = new { type = "string", description = "Optional locally allowed Ollama model name." },
            maxOutputChars = new { type = "integer", minimum = 256, maximum = 200000 },
            timeoutSeconds = new { type = "integer", minimum = 1, maximum = 600 }
        });
    }

    public IReadOnlyList<string> Validate(LocalSummarizeRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Text, "text");
        LocalInferenceToolSupport.ValidateOptionalGenerationControls(
            errors,
            temperature: null,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(LocalSummarizeRequest request, CancellationToken ct)
    {
        var prompt = BuildPrompt(request);
        var inferenceRequest = new LocalInferenceRequest(
            operation: "summarize",
            prompt: prompt,
            systemPrompt: "You are a local coding assistant. Produce compact, factual summaries. Do not invent details.",
            model: request.Model,
            temperature: 0.1d,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);

        return LocalInferenceToolSupport.CompleteAsync(Name, _inferenceService, _logger, inferenceRequest, ct);
    }

    private static string BuildPrompt(LocalSummarizeRequest request)
    {
        var focus = string.IsNullOrWhiteSpace(request.Focus)
            ? "Summarize the important implementation facts, risks, and follow-up items."
            : request.Focus.Trim();

        return $"Focus: {focus}\n\nText:\n{request.Text}";
    }
}
