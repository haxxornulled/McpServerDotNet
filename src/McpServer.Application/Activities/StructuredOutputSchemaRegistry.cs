using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
namespace McpServer.Application.Activities;

public sealed class StructuredOutputSchemaRegistry : IStructuredOutputSchemaRegistry
{
    private readonly IReadOnlyDictionary<string, StructuredOutputSchemaDto> _byName;
    private readonly IReadOnlyDictionary<ActivityKind, StructuredOutputSchemaDto> _byActivity;

    public StructuredOutputSchemaRegistry()
    {
        var schemas = new[]
        {
            BuildSchema(ActivityKind.DeepCodeReview, "deep_code_review_result", "Codex-style enterprise code review findings with severity, evidence, fix order, and build/test recommendations.", DeepCodeReviewCoreSchema()),
            BuildSchema(ActivityKind.BuildFix, "build_fix_result", "Build failure diagnosis and smallest safe fix plan.", BuildFixCoreSchema()),
            BuildSchema(ActivityKind.TestFailureAnalysis, "test_failure_analysis_result", "Test failure diagnosis with likely cause, affected files, and next validation steps.", TestFailureCoreSchema()),
            BuildSchema(ActivityKind.ImplementationPlan, "implementation_plan_result", "Implementation plan with architecture impact, files, tests, risks, and commands.", ImplementationPlanCoreSchema()),
            BuildSchema(ActivityKind.CodePatch, "code_patch_result", "Code patch result with full file contents or precise patch guidance.", CodePatchCoreSchema()),
            BuildSchema(ActivityKind.Diagnostic, "diagnostic_result", "Runtime or transport diagnostic result with causes, evidence, and recommended fix order.", DiagnosticCoreSchema()),
            BuildSchema(ActivityKind.ArchitectureReview, "architecture_review_result", "Architecture review findings focused on boundaries, dependencies, and maintainability.", DeepCodeReviewCoreSchema()),
            BuildSchema(ActivityKind.SecurityReview, "security_review_result", "Security review findings focused on sandboxing, secrets, path traversal, SSRF, shell, and auth boundaries.", DeepCodeReviewCoreSchema()),
            BuildSchema(ActivityKind.RefactorPlan, "refactor_plan_result", "Refactor plan with incremental steps and safety checks.", ImplementationPlanCoreSchema()),
            BuildSchema(ActivityKind.Documentation, "documentation_result", "Documentation work result with files and content plan.", ImplementationPlanCoreSchema()),
            BuildSchema(ActivityKind.CommandPlan, "command_plan_result", "Safe command plan with purpose, working directory, and risk notes.", CommandPlanCoreSchema()),
            BuildSchema(ActivityKind.Validation, "validation_result", "Build/test validation result summary and next action.", ValidationCoreSchema()),
            BuildSchema(ActivityKind.WorkspaceSetup, "workspace_setup_result", "Workspace/project-root setup diagnosis and requested tool sequence.", DiagnosticCoreSchema()),
            BuildSchema(ActivityKind.Explain, "explain_result", "Plain explanation result for non-code-patch activity.", ExplainCoreSchema())
        };

        _byName = schemas.ToDictionary(static schema => schema.SchemaName, StringComparer.OrdinalIgnoreCase);
        _byActivity = schemas.ToDictionary(static schema => schema.Activity);
    }

    public IReadOnlyList<StructuredOutputSchemaDto> ListSchemas() => _byName.Values.OrderBy(static x => x.SchemaName, StringComparer.OrdinalIgnoreCase).ToArray();

    public Fin<StructuredOutputSchemaDto> GetSchema(string schemaName) =>
        _byName.TryGetValue(schemaName, out var schema)
            ? schema
            : Error.New($"Unknown structured output schema: {schemaName}");

    public Fin<StructuredOutputSchemaDto> GetSchema(ActivityKind activity) =>
        _byActivity.TryGetValue(activity, out var schema)
            ? schema
            : Error.New($"No structured output schema is registered for activity {activity}.");

    private static StructuredOutputSchemaDto BuildSchema(ActivityKind activity, string name, string description, object coreSchema)
    {
        var responseFormat = JsonSerializer.SerializeToElement(new
        {
            type = "json_schema",
            json_schema = new
            {
                name,
                strict = true,
                schema = coreSchema
            }
        });

        return new StructuredOutputSchemaDto(name, activity, description, Strict: true, responseFormat);
    }

    private static object BaseObject(object properties, string[] required) => new
    {
        type = "object",
        additionalProperties = false,
        properties,
        required
    };

    private static object DeepCodeReviewCoreSchema() => BaseObject(new
    {
        reviewSummary = new { type = "string" },
        overallRisk = new { type = "string", @enum = new[] { "low", "medium", "high", "critical" } },
        confidence = new { type = "number", minimum = 0, maximum = 1 },
        reviewedScope = BaseObject(new
        {
            workspaceRoot = new { type = "string" },
            projectRoot = new { type = "string" },
            filesReviewed = new { type = "array", items = new { type = "string" } },
            diffReviewed = new { type = "boolean" },
            buildOutputReviewed = new { type = "boolean" },
            testOutputReviewed = new { type = "boolean" }
        }, ["workspaceRoot", "projectRoot", "filesReviewed", "diffReviewed", "buildOutputReviewed", "testOutputReviewed"]),
        findings = new
        {
            type = "array",
            items = BaseObject(new
            {
                id = new { type = "string" },
                severity = new { type = "string", @enum = new[] { "critical", "high", "medium", "low", "nice_to_have" } },
                category = new { type = "string", @enum = new[] { "correctness", "security", "reliability", "performance", "concurrency", "architecture", "observability", "maintainability", "testing", "developer_experience", "build_ci", "documentation" } },
                title = new { type = "string" },
                filePath = new { type = "string" },
                lineHint = new { type = "string" },
                problem = new { type = "string" },
                whyItMatters = new { type = "string" },
                recommendedFix = new { type = "string" },
                examplePatch = new { type = "string" },
                requiresTest = new { type = "boolean" },
                suggestedTests = new { type = "array", items = new { type = "string" } },
                cleanArchitectureBoundaryImpacted = new { type = "boolean" },
                securityImpact = new { type = "boolean" },
                observabilityImpact = new { type = "boolean" },
                confidence = new { type = "number", minimum = 0, maximum = 1 }
            }, ["id", "severity", "category", "title", "filePath", "lineHint", "problem", "whyItMatters", "recommendedFix", "examplePatch", "requiresTest", "suggestedTests", "cleanArchitectureBoundaryImpacted", "securityImpact", "observabilityImpact", "confidence"])
        },
        positiveSignals = new { type = "array", items = new { type = "string" } },
        missingContext = new { type = "array", items = new { type = "string" } },
        recommendedFixOrder = new { type = "array", items = new { type = "string" } },
        buildAndTestRecommendation = BaseObject(new
        {
            shouldBuild = new { type = "boolean" },
            shouldRunUnitTests = new { type = "boolean" },
            shouldRunIntegrationTests = new { type = "boolean" },
            commands = new { type = "array", items = new { type = "string" } }
        }, ["shouldBuild", "shouldRunUnitTests", "shouldRunIntegrationTests", "commands"])
    }, ["reviewSummary", "overallRisk", "confidence", "reviewedScope", "findings", "positiveSignals", "missingContext", "recommendedFixOrder", "buildAndTestRecommendation"]);

    private static object ImplementationPlanCoreSchema() => BaseObject(new
    {
        goal = new { type = "string" },
        assumptions = new { type = "array", items = new { type = "string" } },
        architectureImpact = new { type = "array", items = new { type = "string" } },
        files = new { type = "array", items = BaseObject(new { path = new { type = "string" }, action = new { type = "string", @enum = new[] { "create", "modify", "delete", "read" } }, purpose = new { type = "string" } }, ["path", "action", "purpose"]) },
        tests = new { type = "array", items = new { type = "string" } },
        commands = new { type = "array", items = new { type = "string" } },
        risks = new { type = "array", items = new { type = "string" } },
        nextStep = new { type = "string" }
    }, ["goal", "assumptions", "architectureImpact", "files", "tests", "commands", "risks", "nextStep"]);

    private static object CodePatchCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        files = new { type = "array", items = BaseObject(new { path = new { type = "string" }, language = new { type = "string" }, action = new { type = "string", @enum = new[] { "create", "replace", "modify", "delete" } }, content = new { type = "string" } }, ["path", "language", "action", "content"]) },
        commands = new { type = "array", items = new { type = "string" } },
        notes = new { type = "array", items = new { type = "string" } }
    }, ["summary", "files", "commands", "notes"]);

    private static object BuildFixCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        rootCause = new { type = "string" },
        affectedFiles = new { type = "array", items = new { type = "string" } },
        fixes = new { type = "array", items = new { type = "string" } },
        commands = new { type = "array", items = new { type = "string" } },
        requiresCodePatch = new { type = "boolean" }
    }, ["summary", "rootCause", "affectedFiles", "fixes", "commands", "requiresCodePatch"]);

    private static object TestFailureCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        failingTests = new { type = "array", items = new { type = "string" } },
        likelyCause = new { type = "string" },
        affectedFiles = new { type = "array", items = new { type = "string" } },
        fixPlan = new { type = "array", items = new { type = "string" } },
        validationCommands = new { type = "array", items = new { type = "string" } }
    }, ["summary", "failingTests", "likelyCause", "affectedFiles", "fixPlan", "validationCommands"]);

    private static object DiagnosticCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        likelyCauses = new { type = "array", items = new { type = "string" } },
        evidence = new { type = "array", items = new { type = "string" } },
        fixOrder = new { type = "array", items = new { type = "string" } },
        commands = new { type = "array", items = new { type = "string" } },
        risk = new { type = "string", @enum = new[] { "low", "medium", "high", "critical" } }
    }, ["summary", "likelyCauses", "evidence", "fixOrder", "commands", "risk"]);

    private static object CommandPlanCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        workingDirectory = new { type = "string" },
        commands = new { type = "array", items = BaseObject(new { command = new { type = "string" }, purpose = new { type = "string" }, risk = new { type = "string", @enum = new[] { "low", "medium", "high", "critical" } } }, ["command", "purpose", "risk"]) },
        safetyNotes = new { type = "array", items = new { type = "string" } }
    }, ["summary", "workingDirectory", "commands", "safetyNotes"]);

    private static object ValidationCoreSchema() => BaseObject(new
    {
        summary = new { type = "string" },
        buildPassed = new { type = "boolean" },
        unitTestsPassed = new { type = "boolean" },
        integrationTestsPassed = new { type = "boolean" },
        failures = new { type = "array", items = new { type = "string" } },
        nextAction = new { type = "string" }
    }, ["summary", "buildPassed", "unitTestsPassed", "integrationTestsPassed", "failures", "nextAction"]);

    private static object ExplainCoreSchema() => BaseObject(new
    {
        answer = new { type = "string" },
        assumptions = new { type = "array", items = new { type = "string" } },
        nextAction = new { type = "string" }
    }, ["answer", "assumptions", "nextAction"]);
}
