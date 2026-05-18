using System.Text.Json;

namespace McpServer.AgentRouter.Host.Protocol.Mcp;

public sealed class McpToolCallRequest
{
    public string? ToolName { get; set; }

    public JsonElement? Arguments { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxOutputChars { get; set; }
}

public sealed class McpToolCallResponse
{
    public string Status { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public bool Allowed { get; set; }

    public string PolicyDecision { get; set; } = string.Empty;

    public string? PolicyReason { get; set; }

    public string TraceId { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public long ElapsedMilliseconds { get; set; }

    public JsonElement? Result { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class McpToolListResponse
{
    public string Status { get; set; } = "ok";

    public string Transport { get; set; } = "stdio";

    public string ProtocolVersion { get; set; } = string.Empty;

    public McpServerDescriptor Server { get; set; } = new();

    public int ToolCount { get; set; }

    public long ElapsedMilliseconds { get; set; }

    public IList<McpToolDescriptor> Tools { get; set; } = new List<McpToolDescriptor>();
}

public sealed class McpServerDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}

public sealed class McpToolDescriptor
{
    public string Name { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public JsonElement? InputSchema { get; set; }
}
