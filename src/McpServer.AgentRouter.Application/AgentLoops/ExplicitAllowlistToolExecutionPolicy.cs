using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Application.AgentLoops;

/// <summary>
/// Enforces loop execution allowlisting rules.
/// </summary>
public sealed class ExplicitAllowlistToolExecutionPolicy : IToolExecutionPolicy
{
    private readonly AgentRouterRuntimeSettings _settings;

    /// <summary>
    /// Initializes a new explicit allowlist policy.
    /// </summary>
    public ExplicitAllowlistToolExecutionPolicy(AgentRouterRuntimeSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Evaluates whether the planned step may execute.
    /// </summary>
    public ValueTask<Fin<ToolExecutionPolicyDecision>> EvaluateAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plannedStep);
        cancellationToken.ThrowIfCancellationRequested();

        var loopOptions = _settings.AgentLoop;

        if (!loopOptions.Enabled)
        {
            return Succeed(new ToolExecutionPolicyDecision
            {
                Allowed = false,
                Decision = "denied",
                Reason = "Autonomous loop execution is disabled."
            });
        }

        if (string.IsNullOrWhiteSpace(plannedStep.Capability))
        {
            return Succeed(new ToolExecutionPolicyDecision
            {
                Allowed = false,
                Decision = "denied",
                Reason = "Planned step did not declare a capability."
            });
        }

        if (loopOptions.RequireExplicitAllowlist)
        {
            var allowedCapabilities = new System.Collections.Generic.HashSet<string>(
                context.AllowedCapabilities,
                StringComparer.OrdinalIgnoreCase);

            if (!allowedCapabilities.Contains(plannedStep.Capability.Trim()))
            {
                return Succeed(new ToolExecutionPolicyDecision
                {
                    Allowed = false,
                    Decision = "denied",
                    Reason = $"Capability '{plannedStep.Capability}' is not allowed for this loop."
                });
            }
        }

        return Succeed(new ToolExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed"
        });
    }

    private static ValueTask<Fin<ToolExecutionPolicyDecision>> Succeed(ToolExecutionPolicyDecision decision)
    {
        return new ValueTask<Fin<ToolExecutionPolicyDecision>>(Fin<ToolExecutionPolicyDecision>.Succ(decision));
    }
}
