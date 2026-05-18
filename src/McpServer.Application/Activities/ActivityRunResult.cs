using System.Text.Json.Serialization;
namespace McpServer.Application.Activities;

public sealed record ActivityRunResult(
    [property: JsonPropertyName("route")] ActivityRoutingResult Route,
    [property: JsonPropertyName("profile")] ActivityProfileDto Profile,
    [property: JsonPropertyName("context")] ActivityContextPacket Context,
    [property: JsonPropertyName("schema")] StructuredOutputSchemaDto Schema,
    [property: JsonPropertyName("requiresModelCall")] bool RequiresModelCall,
    [property: JsonPropertyName("nextAction")] string NextAction,
    [property: JsonPropertyName("runBuildRequested")] bool RunBuildRequested,
    [property: JsonPropertyName("runTestsRequested")] bool RunTestsRequested,
    [property: JsonPropertyName("sessionState")] ActivitySessionState SessionState);
