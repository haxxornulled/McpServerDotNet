using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Inference;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

public sealed class LocalCompleteToolHandler : IToolHandler<LocalCompleteRequest>
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LocalCompleteToolHandler> _logger;

    public LocalCompleteToolHandler(
        ILocalInferenceService inferenceService,
        ILogger<LocalCompleteToolHandler> logger)
    {
        _inferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "inference.local_complete";

    public string Description => "Delegates a bounded prompt to the locally configured Ollama model and returns the response.";

    public JsonElement GetInputSchema()
    {
        return LocalInferenceToolSupport.CreateTextGenerationSchema("prompt", new
        {
            prompt = new { type = "string", description = "The user prompt to send to the local model." },
            systemPrompt = new { type = "string", description = "Optional system/developer guidance for the local model." },
            model = new { type = "string", description = "Optional locally allowed Ollama model name." },
            temperature = new { type = "number", minimum = 0, maximum = 2 },
            maxOutputChars = new { type = "integer", minimum = 256, maximum = 200000 },
            timeoutSeconds = new { type = "integer", minimum = 1, maximum = 600 }
        });
    }

    public IReadOnlyList<string> Validate(LocalCompleteRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Prompt, "prompt");
        LocalInferenceToolSupport.ValidateOptionalGenerationControls(
            errors,
            request.Temperature,
            request.MaxOutputChars,
            request.TimeoutSeconds);
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(LocalCompleteRequest request, CancellationToken ct)
    {
        var inferenceRequest = new LocalInferenceRequest(
            operation: "complete",
            prompt: request.Prompt,
            systemPrompt: request.SystemPrompt,
            model: request.Model,
            temperature: request.Temperature,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);

        return LocalInferenceToolSupport.CompleteAsync(Name, _inferenceService, _logger, inferenceRequest, ct);
    }
}
