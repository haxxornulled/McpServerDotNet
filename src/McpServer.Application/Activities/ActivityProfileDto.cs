using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityProfileDto(
    [property: JsonPropertyName("activity")] ActivityKind Activity,
    [property: JsonPropertyName("systemPromptKey")] string SystemPromptKey,
    [property: JsonPropertyName("schemaName")] string SchemaName,
    [property: JsonPropertyName("useStructuredOutput")] bool UseStructuredOutput,
    [property: JsonPropertyName("needsWorkspaceStatus")] bool NeedsWorkspaceStatus,
    [property: JsonPropertyName("needsGitStatus")] bool NeedsGitStatus,
    [property: JsonPropertyName("needsGitDiff")] bool NeedsGitDiff,
    [property: JsonPropertyName("needsBuildOutput")] bool NeedsBuildOutput,
    [property: JsonPropertyName("needsTestOutput")] bool NeedsTestOutput,
    [property: JsonPropertyName("allowsShellExecution")] bool AllowsShellExecution,
    [property: JsonPropertyName("maxContextBytes")] int MaxContextBytes);
