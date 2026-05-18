using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface IModelProfileResolver
{
    ValueTask<Fin<ModelProfile>> ResolveAsync(string? profileName, CancellationToken cancellationToken);

    IReadOnlyList<ModelProfile> ListProfiles();
}
