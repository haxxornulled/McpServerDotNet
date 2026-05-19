using System.Text.Json;

namespace McpServer.AgentRouter.Application.Mcp;

/// <summary>
/// Captures a snapshot of the MCP tool catalog.
/// </summary>
public sealed class McpToolCatalogSnapshot
{
    /// <summary>
    /// Gets or sets the transport used to obtain the catalog.
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Gets or sets the protocol version.
    /// </summary>
    public string ProtocolVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server name.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the server version.
    /// </summary>
    public string ServerVersion { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the elapsed time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the listed tools.
    /// </summary>
    public IList<McpToolCatalogItem> Tools { get; set; } = new List<McpToolCatalogItem>();
}

/// <summary>
/// Describes a single MCP tool catalog item.
/// </summary>
public sealed class McpToolCatalogItem
{
    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the input schema.
    /// </summary>
    public JsonElement? InputSchema { get; set; }
}
