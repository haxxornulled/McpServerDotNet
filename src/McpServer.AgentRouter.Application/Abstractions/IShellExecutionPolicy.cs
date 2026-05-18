using LanguageExt;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IShellExecutionPolicy
{
    ValueTask<Fin<ShellExecutionPolicyDecision>> EvaluateAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken);
}
