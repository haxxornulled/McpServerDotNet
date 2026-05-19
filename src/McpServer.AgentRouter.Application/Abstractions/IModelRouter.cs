using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Routes model requests to the appropriate provider implementation.
/// </summary>
public interface IModelRouter
{
    /// <summary>
    /// Executes a single-turn completion request.
    /// </summary>
    ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes a streaming completion request.
    /// </summary>
    ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        CancellationToken cancellationToken);
}
