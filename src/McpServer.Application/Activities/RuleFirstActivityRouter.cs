namespace McpServer.Application.Activities;

public sealed class RuleFirstActivityRouter(IActivityProfileRegistry profiles) : IActivityRouter
{
    public ValueTask<ActivityRoutingResult> RouteAsync(string userRequest, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userRequest);
        var text = userRequest.ToLowerInvariant();

        var activity = DetermineActivity(text);
        var profile = profiles.GetProfile(activity).Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        var result = new ActivityRoutingResult(
            activity,
            ConfidenceFor(activity, text),
            ReasonFor(activity),
            profile.NeedsWorkspaceStatus,
            profile.AllowsShellExecution,
            profile.UseStructuredOutput,
            profile.SchemaName);

        return ValueTask.FromResult(result);
    }

    private static ActivityKind DetermineActivity(string text)
    {
        if (ContainsAny(text, "test failed", "xunit", "assert.", "failed:", "testerror", "integrationtests", "unittests"))
        {
            return ActivityKind.TestFailureAnalysis;
        }

        if (ContainsAny(text, "build failed", "does not compile", "compiler error", "cs0", "msb", "restore failed"))
        {
            return ActivityKind.BuildFix;
        }

        if (ContainsAny(text, "deep review", "deep-dive", "deep dive", "code review", "enterprise review", "codex-style", "audit", "review this"))
        {
            return ActivityKind.DeepCodeReview;
        }

        if (ContainsAny(text, "security", "harden", "sandbox", "ssrf", "path traversal", "credential", "secret"))
        {
            return ActivityKind.SecurityReview;
        }

        if (ContainsAny(text, "lm studio", "mcp", "transport", "hang", "hanging", "post /", "get /", "endpoint", "bridge"))
        {
            return ActivityKind.Diagnostic;
        }

        if (ContainsAny(text, "workspace", "project root", "open folder", "select folder", "allowed root"))
        {
            return ActivityKind.WorkspaceSetup;
        }

        if (ContainsAny(text, "implement", "code all", "code this", "add", "create", "fix it", "proceed", "continue"))
        {
            return ActivityKind.CodePatch;
        }

        if (ContainsAny(text, "architecture", "design", "boundaries", "system design"))
        {
            return ActivityKind.ArchitectureReview;
        }

        if (ContainsAny(text, "plan", "roadmap", "approach", "how should we"))
        {
            return ActivityKind.ImplementationPlan;
        }

        if (ContainsAny(text, "docs", "documentation", "readme", "document"))
        {
            return ActivityKind.Documentation;
        }

        if (ContainsAny(text, "command", "shell", "install", "configure", "setup"))
        {
            return ActivityKind.CommandPlan;
        }

        return ActivityKind.Explain;
    }

    private static double ConfidenceFor(ActivityKind activity, string text) => activity switch
    {
        ActivityKind.Explain => 0.60,
        ActivityKind.CodePatch when ContainsAny(text, "proceed", "continue") => 0.72,
        ActivityKind.Diagnostic => 0.88,
        ActivityKind.BuildFix => 0.92,
        ActivityKind.TestFailureAnalysis => 0.92,
        ActivityKind.DeepCodeReview => 0.90,
        _ => 0.84
    };

    private static string ReasonFor(ActivityKind activity) => activity switch
    {
        ActivityKind.DeepCodeReview => "The request asks for code review, audit, or Codex-style deep analysis.",
        ActivityKind.BuildFix => "The request appears to include or reference compiler/build failures.",
        ActivityKind.TestFailureAnalysis => "The request appears to include or reference test failures.",
        ActivityKind.CodePatch => "The request asks for implementation or continuation of code changes.",
        ActivityKind.WorkspaceSetup => "The request is about active workspace/project folder behavior.",
        ActivityKind.Diagnostic => "The request is about diagnosing MCP, LM Studio, transport, endpoint, or runtime behavior.",
        ActivityKind.SecurityReview => "The request asks for security or hardening analysis.",
        ActivityKind.ArchitectureReview => "The request asks for architecture or system design review.",
        ActivityKind.Documentation => "The request asks for documentation-oriented work.",
        ActivityKind.CommandPlan => "The request asks for setup or command guidance.",
        _ => "No stronger activity rule matched; defaulting to explanation."
    };

    private static bool ContainsAny(string text, params string[] values)
    {
        foreach (var value in values)
        {
            if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
