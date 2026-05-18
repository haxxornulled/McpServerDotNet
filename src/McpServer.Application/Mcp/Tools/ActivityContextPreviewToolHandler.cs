using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Activities;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class ActivityContextPreviewToolHandler(
    IActivityRouter activityRouter,
    IActivityProfileRegistry profileRegistry,
    IActivityContextBuilder contextBuilder,
    ILogger<ActivityContextPreviewToolHandler> logger) : IToolHandler<ActivityContextPreviewRequest>
{
    public string Name => "activity.context.preview";
    public string Description => "Builds a bounded activity context packet preview for a user request without calling LM Studio.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                request = new { type = "string", minLength = 1 },
                activity = new { type = "string", description = "Optional activity override such as deep_code_review, build_fix, code_patch, or auto." },
                maxContextBytes = new { type = "integer", minimum = 0, maximum = 250000, @default = 0 }
            },
            required = new[] { "request" }
        });

    public IReadOnlyList<string> Validate(ActivityContextPreviewRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Request, "request");
        ToolRequestValidation.RequireRange(errors, request.MaxContextBytes, "maxContextBytes", 0, 250000);
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(ActivityContextPreviewRequest request, CancellationToken ct)
    {
        var route = await ResolveRouteAsync(request.Request, request.Activity, ct).ConfigureAwait(false);
        if (route.IsFail)
        {
            return route.Match<Fin<CallToolResult>>(Succ: _ => throw new InvalidOperationException("Expected route failure."), Fail: error => error);
        }

        var resolvedRoute = route.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var profile = profileRegistry.GetProfile(resolvedRoute.Activity).Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var context = await contextBuilder.BuildAsync(resolvedRoute, profile, request.Request, request.MaxContextBytes > 0 ? request.MaxContextBytes : null, ct).ConfigureAwait(false);
        var result = new ActivityContextPreviewResult(resolvedRoute, profile, context);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

        logger.LogInformation("Tool {ToolName} built context preview for activity {Activity} with {Bytes} bytes", Name, resolvedRoute.Activity, context.ApproximateBytes);
        return new CallToolResult([new ContentItem("text", json)], StructuredContent: result);
    }

    private async ValueTask<Fin<ActivityRoutingResult>> ResolveRouteAsync(string userRequest, string? activity, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(activity) && !activity.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = profileRegistry.ParseActivity(activity);
            if (parsed.IsFail)
            {
                return parsed.Match<Fin<ActivityRoutingResult>>(Succ: _ => throw new InvalidOperationException("Expected activity parse failure."), Fail: error => error);
            }

            var kind = parsed.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
            var profile = profileRegistry.GetProfile(kind).Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
            return new ActivityRoutingResult(kind, 1.0, "Activity was explicitly provided by the caller.", profile.NeedsWorkspaceStatus, profile.AllowsShellExecution, profile.UseStructuredOutput, profile.SchemaName);
        }

        return await activityRouter.RouteAsync(userRequest, ct).ConfigureAwait(false);
    }
}
