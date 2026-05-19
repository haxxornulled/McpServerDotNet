namespace McpServer.AgentRouter.Application.Mcp;

/// <summary>
/// Represents the raw outcome of an MCP tool call.
/// </summary>
public sealed class McpToolCallResult
{
    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport used for the call.
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Gets or sets a value indicating whether the result represents an error.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Gets or sets the raw response payload.
    /// </summary>
    public string RawResult { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text content extracted from the result.
    /// </summary>
    public IReadOnlyList<string> ContentText { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}
