using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Application.Mcp;

/// <summary>
/// Applies configuration-based policy checks to MCP tool calls.
/// </summary>
public sealed class McpToolCallPolicy : IMcpToolCallPolicy
{
    private readonly AgentRouterRuntimeSettings _settings;

    /// <summary>
    /// Initializes a new MCP tool call policy.
    /// </summary>
    public McpToolCallPolicy(AgentRouterRuntimeSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Evaluates the supplied MCP tool call command.
    /// </summary>
    public ValueTask<Fin<McpToolCallPolicyDecision>> EvaluateAsync(
        McpToolCallCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _settings.ToolExecution;

        if (!options.Enabled)
        {
            return Succeed(false, "denied", "MCP tool execution is disabled by configuration.");
        }

        if (string.IsNullOrWhiteSpace(command.ToolName))
        {
            return Succeed(false, "denied", "toolName is required.");
        }

        if (!options.RequireExplicitAllowlist)
        {
            return Succeed(true, "allowed", null);
        }

        var allowedTools = new System.Collections.Generic.HashSet<string>(
            options.AllowedTools,
            StringComparer.OrdinalIgnoreCase);

        if (!allowedTools.Contains(command.ToolName.Trim()))
        {
            return Succeed(
                false,
                "denied",
                $"MCP tool '{command.ToolName}' is not in AgentRouter:ToolExecution:AllowedTools.");
        }

        return Succeed(true, "allowed", null);
    }

    private static ValueTask<Fin<McpToolCallPolicyDecision>> Succeed(
        bool allowed,
        string decision,
        string? reason)
    {
        return new ValueTask<Fin<McpToolCallPolicyDecision>>(
            Fin<McpToolCallPolicyDecision>.Succ(new McpToolCallPolicyDecision
            {
                Allowed = allowed,
                Decision = decision,
                Reason = reason
            }));
    }
}
