using System.Text.Json.Serialization;
namespace McpServer.Application.Activities;

public sealed record ActivityContextPreviewResult(
    [property: JsonPropertyName("route")] ActivityRoutingResult Route,
    [property: JsonPropertyName("profile")] ActivityProfileDto Profile,
    [property: JsonPropertyName("context")] ActivityContextPacket Context);
