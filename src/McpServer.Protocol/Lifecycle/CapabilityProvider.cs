using McpServer.Protocol.Lifecycle;

namespace McpServer.Protocol.Lifecycle;

public sealed class CapabilityProvider
{
    private static readonly IReadOnlyDictionary<string, object?> ExperimentalCapabilities =
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["mcpServer.workspaceChangedNotifications"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["method"] = "notifications/workspace/changed"
            }
        };

    private static readonly ServerCapabilitiesDto Capabilities = new(
        Tools: ToolsCapabilityDto.StaticList,
        Resources: ResourcesCapabilityDto.StaticListNoSubscribe,
        Prompts: PromptsCapabilityDto.StaticList,
        Experimental: ExperimentalCapabilities);

    public ServerCapabilitiesDto GetCapabilities()
    {
        return Capabilities;
    }
}
