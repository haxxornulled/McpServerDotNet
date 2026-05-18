using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Activities;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class ActivityRunToolHandler(
    IActivityRouter activityRouter,
    IActivityProfileRegistry profileRegistry,
    IStructuredOutputSchemaRegistry schemaRegistry,
    IActivityContextBuilder contextBuilder,
    IActivitySessionStateStore sessionStateStore,
    ILogger<ActivityRunToolHandler> logger) : IToolHandler<ActivityRunRequest>
{
    public string Name => "activity.run";
    public string Description => "Runs dynamic activity orchestration: route request, build context packet, select structured-output schema, and return the LM Studio-ready model call payload. This tool does not call LM Studio directly.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                request = new { type = "string", minLength = 1 },
                activity = new { type = "string", description = "Optional activity override, or auto." },
                maxContextBytes = new { type = "integer", minimum = 0, maximum = 250000, @default = 0 },
                runBuild = new { type = "boolean", @default = false, description = "Caller intent to include a build step. activity.run records this but does not execute shell commands." },
                runTests = new { type = "boolean", @default = false, description = "Caller intent to include tests. activity.run records this but does not execute shell commands." }
            },
            required = new[] { "request" }
        });

    public IReadOnlyList<string> Validate(ActivityRunRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Request, "request");
        ToolRequestValidation.RequireRange(errors, request.MaxContextBytes, "maxContextBytes", 0, 250000);
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(ActivityRunRequest request, CancellationToken ct)
    {
        var route = await ResolveRouteAsync(request.Request, request.Activity, ct).ConfigureAwait(false);
        if (route.IsFail)
        {
            return route.Match<Fin<CallToolResult>>(Succ: _ => throw new InvalidOperationException("Expected route failure."), Fail: error => error);
        }

        var resolvedRoute = route.Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var profile = profileRegistry.GetProfile(resolvedRoute.Activity).Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var schema = schemaRegistry.GetSchema(resolvedRoute.Activity).Match(Succ: value => value, Fail: error => throw new InvalidOperationException(error.Message));
        var context = await contextBuilder.BuildAsync(resolvedRoute, profile, request.Request, request.MaxContextBytes > 0 ? request.MaxContextBytes : null, ct).ConfigureAwait(false);
        var state = sessionStateStore.Update(resolvedRoute.Activity, request.Request, schema.SchemaName, $"Prepared {ActivityProfileRegistry.ToSnakeCase(resolvedRoute.Activity)} context packet with {context.ApproximateBytes} bytes.");

        var result = new ActivityRunResult(
            resolvedRoute,
            profile,
            context,
            schema,
            RequiresModelCall: true,
            NextAction: "Send context.contextMarkdown to LM Studio with schema.responseFormat as response_format. Validate the model response against schema.responseFormat.json_schema.schema.",
            RunBuildRequested: request.RunBuild,
            RunTestsRequested: request.RunTests,
            SessionState: state);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        logger.LogInformation("Tool {ToolName} prepared activity run {Activity} using schema {SchemaName}", Name, resolvedRoute.Activity, schema.SchemaName);
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
