namespace McpServer.AgentRouter.Application.Mcp;

public sealed class McpToolCallResult
{
    public string ToolName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public bool IsError { get; set; }

    public string RawResult { get; set; } = string.Empty;

    public IList<string> ContentText { get; set; } = new List<string>();

    public long ElapsedMilliseconds { get; set; }
}
