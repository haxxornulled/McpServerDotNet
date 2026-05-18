using LanguageExt;
using McpServer.Application.Inference;

namespace McpServer.Application.Abstractions.Inference;

public interface ILocalInferenceService
{
    ValueTask<Fin<LocalInferenceResponse>> CompleteAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken);

    ValueTask<Fin<LocalInferenceStatus>> GetStatusAsync(
        CancellationToken cancellationToken);
}
