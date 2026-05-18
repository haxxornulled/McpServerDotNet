using System.Text.Json;

namespace McpServer.AgentRouter.Application.Mcp;

public sealed class McpToolCatalogSnapshot
{
    public string Transport { get; set; } = "stdio";

    public string ProtocolVersion { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    public string ServerVersion { get; set; } = string.Empty;

    public long ElapsedMilliseconds { get; set; }

    public IList<McpToolCatalogItem> Tools { get; set; } = new List<McpToolCatalogItem>();
}

public sealed class McpToolCatalogItem
{
    public string Name { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public JsonElement? InputSchema { get; set; }
}
