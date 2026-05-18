using System.Text.Json;

namespace McpServer.AgentRouter.Domain.Mcp;

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

public static class McpToolCallStatusNames
{
    public const string Denied = "denied";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed class McpToolCallCommand
{
    public string TraceId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public JsonElement Arguments { get; set; }

    public int TimeoutSeconds { get; set; }

    public int MaxOutputChars { get; set; }
}

public sealed class McpToolCallPolicyDecision
{
    public bool Allowed { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

public sealed class McpToolInvocationResult
{
    public string Status { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string Transport { get; set; } = "stdio";

    public long ElapsedMilliseconds { get; set; }

    public JsonElement? Result { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class McpToolCallTraceRecord
{
    public string TraceId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public bool Allowed { get; set; }

    public string PolicyDecision { get; set; } = string.Empty;

    public string? PolicyReason { get; set; }

    public string Status { get; set; } = string.Empty;

    public long ElapsedMilliseconds { get; set; }

    public string? ErrorMessage { get; set; }

    public JsonElement Arguments { get; set; }

    public JsonElement? Result { get; set; }
}
