using LanguageExt;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ISshExecutionPolicy
{
    ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(
        SshExecutionRequest request,
        CancellationToken cancellationToken);
}
