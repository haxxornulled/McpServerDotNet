using LanguageExt;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Resolves model profile names to runtime profile definitions.
/// </summary>
public interface IModelProfileResolver
{
    /// <summary>
    /// Resolves a profile by name, or the default profile when no name is supplied.
    /// </summary>
    ValueTask<Fin<ModelProfile>> ResolveAsync(string? profileName, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the available model profiles.
    /// </summary>
    IReadOnlyList<ModelProfile> ListProfiles();
}
