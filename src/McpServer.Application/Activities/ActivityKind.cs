using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

[JsonConverter(typeof(JsonStringEnumConverter<ActivityKind>))]
public enum ActivityKind
{
    Explain,
    WorkspaceSetup,
    DeepCodeReview,
    ImplementationPlan,
    CodePatch,
    BuildFix,
    TestFailureAnalysis,
    Diagnostic,
    ArchitectureReview,
    SecurityReview,
    RefactorPlan,
    Documentation,
    CommandPlan,
    Validation
}
