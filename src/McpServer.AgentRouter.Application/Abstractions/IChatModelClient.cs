using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IChatModelClient
{
    string ProviderName { get; }

    ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken);

    ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken);
}
