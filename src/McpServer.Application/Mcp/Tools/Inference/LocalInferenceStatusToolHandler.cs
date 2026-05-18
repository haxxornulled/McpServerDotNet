using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Inference;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools.Inference;

public sealed class LocalInferenceStatusToolHandler : IToolHandler<LocalInferenceStatusRequest>
{
    private readonly ILocalInferenceService _inferenceService;
    private readonly ILogger<LocalInferenceStatusToolHandler> _logger;

    public LocalInferenceStatusToolHandler(
        ILocalInferenceService inferenceService,
        ILogger<LocalInferenceStatusToolHandler> logger)
    {
        _inferenceService = inferenceService ?? throw new ArgumentNullException(nameof(inferenceService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "inference.local_status";

    public string Description => "Checks whether the configured local Ollama endpoint is reachable and lists visible local models.";

    public JsonElement GetInputSchema()
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new { }
        });
    }

    public async ValueTask<Fin<CallToolResult>> Handle(LocalInferenceStatusRequest request, CancellationToken ct)
    {
        var result = await _inferenceService.GetStatusAsync(ct).ConfigureAwait(false);
        return result.Map(status =>
        {
            _logger.LogInformation(
                "Tool {ToolName} completed local inference status check. Reachable: {Reachable}",
                Name,
                status.ServerReachable);

            var text = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
            return new CallToolResult(
                new[] { new ContentItem("text", text) },
                StructuredContent: status);
        });
    }
}
