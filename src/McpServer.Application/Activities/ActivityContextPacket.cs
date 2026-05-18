using System.Text.Json.Serialization;

namespace McpServer.Application.Activities;

public sealed record ActivityContextPacket(
    [property: JsonPropertyName("activity")] ActivityKind Activity,
    [property: JsonPropertyName("userRequest")] string UserRequest,
    [property: JsonPropertyName("workspaceRoot")] string WorkspaceRoot,
    [property: JsonPropertyName("projectRoot")] string ProjectRoot,
    [property: JsonPropertyName("includedFiles")] IReadOnlyList<string> IncludedFiles,
    [property: JsonPropertyName("contextMarkdown")] string ContextMarkdown,
    [property: JsonPropertyName("approximateBytes")] int ApproximateBytes,
    [property: JsonPropertyName("truncated")] bool Truncated);
