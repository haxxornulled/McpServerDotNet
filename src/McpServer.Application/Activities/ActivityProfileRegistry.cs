using LanguageExt;
using LanguageExt.Common;
namespace McpServer.Application.Activities;

public sealed class ActivityProfileRegistry : IActivityProfileRegistry
{
    private readonly IReadOnlyDictionary<ActivityKind, ActivityProfileDto> _profiles;

    public ActivityProfileRegistry()
    {
        var profiles = new[]
        {
            Profile(ActivityKind.Explain, "explain", "explain_result", false, false, false, false, false, false, false, 40_000),
            Profile(ActivityKind.WorkspaceSetup, "workspace_setup", "workspace_setup_result", true, true, false, false, false, false, false, 60_000),
            Profile(ActivityKind.DeepCodeReview, "deep_code_review", "deep_code_review_result", true, true, true, true, true, true, true, 160_000),
            Profile(ActivityKind.ImplementationPlan, "implementation_plan", "implementation_plan_result", true, true, true, true, false, false, false, 120_000),
            Profile(ActivityKind.CodePatch, "code_patch", "code_patch_result", true, true, true, true, false, false, false, 140_000),
            Profile(ActivityKind.BuildFix, "build_fix", "build_fix_result", true, true, true, false, true, false, true, 120_000),
            Profile(ActivityKind.TestFailureAnalysis, "test_failure_analysis", "test_failure_analysis_result", true, true, true, true, false, true, true, 120_000),
            Profile(ActivityKind.Diagnostic, "diagnostic", "diagnostic_result", true, true, false, false, false, false, false, 80_000),
            Profile(ActivityKind.ArchitectureReview, "architecture_review", "architecture_review_result", true, true, true, true, false, false, false, 140_000),
            Profile(ActivityKind.SecurityReview, "security_review", "security_review_result", true, true, true, true, false, false, false, 140_000),
            Profile(ActivityKind.RefactorPlan, "refactor_plan", "refactor_plan_result", true, true, true, true, false, false, false, 120_000),
            Profile(ActivityKind.Documentation, "documentation", "documentation_result", true, true, true, true, false, false, false, 100_000),
            Profile(ActivityKind.CommandPlan, "command_plan", "command_plan_result", true, true, false, false, false, false, false, 60_000),
            Profile(ActivityKind.Validation, "validation", "validation_result", true, true, true, false, true, true, true, 100_000)
        };

        _profiles = profiles.ToDictionary(static profile => profile.Activity);
    }

    public IReadOnlyList<ActivityProfileDto> ListProfiles() => _profiles.Values.OrderBy(static x => x.Activity).ToArray();

    public Fin<ActivityProfileDto> GetProfile(ActivityKind activity) =>
        _profiles.TryGetValue(activity, out var profile)
            ? profile
            : Error.New($"No activity profile is registered for {activity}.");

    public Fin<ActivityKind> ParseActivity(string? activity)
    {
        if (string.IsNullOrWhiteSpace(activity) || activity.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return Error.New("Activity was not provided.");
        }

        var normalized = activity.Replace("-", "_", StringComparison.Ordinal).Trim();
        foreach (var value in Enum.GetValues<ActivityKind>())
        {
            if (string.Equals(ToSnakeCase(value), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return Error.New($"Unknown activity '{activity}'.");
    }

    private static ActivityProfileDto Profile(
        ActivityKind activity,
        string systemPromptKey,
        string schemaName,
        bool useStructuredOutput,
        bool needsWorkspaceStatus,
        bool needsGitStatus,
        bool needsGitDiff,
        bool needsBuildOutput,
        bool needsTestOutput,
        bool allowsShellExecution,
        int maxContextBytes) =>
        new(activity, systemPromptKey, schemaName, useStructuredOutput, needsWorkspaceStatus, needsGitStatus, needsGitDiff, needsBuildOutput, needsTestOutput, allowsShellExecution, maxContextBytes);

    public static string ToSnakeCase(ActivityKind activity)
    {
        var text = activity.ToString();
        var chars = new List<char>(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i > 0 && char.IsUpper(c))
            {
                chars.Add('_');
            }
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }
}
