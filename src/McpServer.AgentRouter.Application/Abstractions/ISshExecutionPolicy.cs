using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Evaluates whether an SSH execution request should be allowed.
/// </summary>
public interface ISshExecutionPolicy
{
    /// <summary>
    /// Evaluates the supplied SSH request.
    /// </summary>
    ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(
        SshExecutionRequest request,
        CancellationToken cancellationToken);
}
