using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Inference;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

public sealed class LocalPlanToolHandler : IToolHandler<LocalPlanRequest>
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LocalPlanToolHandler> _logger;

    public LocalPlanToolHandler(
        ILocalInferenceService inferenceService,
        ILogger<LocalPlanToolHandler> logger)
    {
        _inferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "inference.local_plan";

    public string Description => "Asks the local Ollama model for a cheap implementation or review plan that Codex can accept, reject, or refine.";

    public JsonElement GetInputSchema()
    {
        return LocalInferenceToolSupport.CreateTextGenerationSchema("goal", new
        {
            goal = new { type = "string", description = "Task goal to plan." },
            context = new { type = "string", description = "Optional repo, file, or architecture context." },
            model = new { type = "string", description = "Optional locally allowed Ollama model name." },
            maxOutputChars = new { type = "integer", minimum = 256, maximum = 200000 },
            timeoutSeconds = new { type = "integer", minimum = 1, maximum = 600 }
        });
    }

    public IReadOnlyList<string> Validate(LocalPlanRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Goal, "goal");
        LocalInferenceToolSupport.ValidateOptionalGenerationControls(
            errors,
            temperature: null,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);
        return errors;
    }

    public ValueTask<Fin<CallToolResult>> Handle(LocalPlanRequest request, CancellationToken ct)
    {
        var context = string.IsNullOrWhiteSpace(request.Context) ? "No additional context supplied." : request.Context.Trim();
        var prompt = $"Goal:\n{request.Goal}\n\nContext:\n{context}\n\nReturn a compact, ordered plan. Flag unknowns. Do not modify files.";

        var inferenceRequest = new LocalInferenceRequest(
            operation: "plan",
            prompt: prompt,
            systemPrompt: "You are a local planning assistant for a senior .NET engineer. Prefer practical, low-risk, testable steps.",
            model: request.Model,
            temperature: 0.2d,
            maxOutputChars: request.MaxOutputChars,
            timeoutSeconds: request.TimeoutSeconds);

        return LocalInferenceToolSupport.CompleteAsync(Name, _inferenceService, _logger, inferenceRequest, ct);
    }
}
