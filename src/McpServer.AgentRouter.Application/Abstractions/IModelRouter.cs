using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IModelRouter
{
    ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken);
}
