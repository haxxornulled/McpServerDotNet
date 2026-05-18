using System.Text.Json.Serialization;

namespace McpServer.Protocol.Notifications;

public sealed record ResourceUpdatedNotificationParams(
    [property: JsonPropertyName("uri")] string Uri);

public sealed record WorkspaceChangeNotificationParams(
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("details")] string? Details = null);
