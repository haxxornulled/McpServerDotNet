using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record ExecRunProcessRequest(
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("arguments")] IEnumerable<string>? Arguments = null,
        [property: JsonPropertyName("working_directory")] string? WorkingDirectory = null,
        [property: JsonPropertyName("timeout_seconds")] int TimeoutSeconds = 30);
}
