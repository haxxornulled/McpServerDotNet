using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Executes chat completion requests against a specific model provider.
/// </summary>
public interface IChatModelClient
{
    /// <summary>
    /// Gets the provider name implemented by the client.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Executes a non-streaming completion request.
    /// </summary>
    ValueTask<Fin<ModelTurnResult>> CompleteAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes a streaming completion request.
    /// </summary>
    ValueTask<Fin<ModelTurnStream>> StreamAsync(
        ModelInvocationRequest request,
        ModelProfile profile,
        CancellationToken cancellationToken);
}
