using System.Text.Json.Serialization;

namespace McpServer.Application.Mcp.Tools
{
    public record SshExecRequest(
        [property: JsonPropertyName("profile")] string Profile,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("working_directory")] string? WorkingDirectory = null);
}
