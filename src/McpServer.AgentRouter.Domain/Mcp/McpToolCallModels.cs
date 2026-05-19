using System.Text.Json;

namespace McpServer.AgentRouter.Domain.Mcp;

/// <summary>
/// Describes an MCP tool call request.
/// </summary>
public sealed class McpToolCallRequest
{
    /// <summary>
    /// Gets or sets the tool name to invoke.
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON arguments for the tool call.
    /// </summary>
    public JsonElement? Arguments { get; set; }

    /// <summary>
    /// Gets or sets the request timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum output size allowed for the tool response.
    /// </summary>
    public int? MaxOutputChars { get; set; }
}

/// <summary>
/// Describes the response produced by an MCP tool call.
/// </summary>
public sealed class McpToolCallResponse
{
    /// <summary>
    /// Gets or sets the final call status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the invoked tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the policy allowed the call.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the policy decision name.
    /// </summary>
    public string PolicyDecision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy denial or approval reason.
    /// </summary>
    public string? PolicyReason { get; set; }

    /// <summary>
    /// Gets or sets the trace identifier associated with the call.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport used for the call.
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the tool result payload.
    /// </summary>
    public JsonElement? Result { get; set; }

    /// <summary>
    /// Gets or sets the error message, if the call failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Provides stable status names for MCP tool calls.
/// </summary>
public static class McpToolCallStatusNames
{
    /// <summary>Denied call status.</summary>
    public const string Denied = "denied";
    /// <summary>Completed call status.</summary>
    public const string Completed = "completed";
    /// <summary>Failed call status.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// Describes an internal command used to execute an MCP tool call.
/// </summary>
public sealed class McpToolCallCommand
{
    /// <summary>
    /// Gets or sets the trace identifier associated with the call.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the raw tool arguments.
    /// </summary>
    public JsonElement Arguments { get; set; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum output characters allowed.
    /// </summary>
    public int MaxOutputChars { get; set; }
}

/// <summary>
/// Represents a policy decision for an MCP tool call.
/// </summary>
public sealed class McpToolCallPolicyDecision
{
    /// <summary>
    /// Gets or sets a value indicating whether the call is allowed.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the decision name.
    /// </summary>
    public string Decision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the reason for the decision.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Describes the raw result of an MCP tool invocation.
/// </summary>
public sealed class McpToolInvocationResult
{
    /// <summary>
    /// Gets or sets the final execution status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the invoked tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the transport used for the invocation.
    /// </summary>
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the raw tool result payload.
    /// </summary>
    public JsonElement? Result { get; set; }

    /// <summary>
    /// Gets or sets the error message, if the invocation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Captures the trace record for an MCP tool call.
/// </summary>
public sealed class McpToolCallTraceRecord
{
    /// <summary>
    /// Gets or sets the trace identifier.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the time the call started.
    /// </summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Gets or sets the time the call completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the policy allowed the call.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the policy decision name.
    /// </summary>
    public string PolicyDecision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy reason.
    /// </summary>
    public string? PolicyReason { get; set; }

    /// <summary>
    /// Gets or sets the final status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the error message, if any.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the original call arguments.
    /// </summary>
    public JsonElement Arguments { get; set; }

    /// <summary>
    /// Gets or sets the result payload, if any.
    /// </summary>
    public JsonElement? Result { get; set; }
}
