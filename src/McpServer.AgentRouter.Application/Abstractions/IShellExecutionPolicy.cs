using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Evaluates whether a shell execution request should be allowed.
/// </summary>
public interface IShellExecutionPolicy
{
    /// <summary>
    /// Evaluates the supplied shell request.
    /// </summary>
    ValueTask<Fin<ShellExecutionPolicyDecision>> EvaluateAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken);
}
