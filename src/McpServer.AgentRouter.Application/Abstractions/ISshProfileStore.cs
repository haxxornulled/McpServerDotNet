using LanguageExt;
using McpServer.AgentRouter.Application.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Loads the configured SSH profile catalog.
/// </summary>
public interface ISshProfileStore
{
    /// <summary>
    /// Loads all available SSH profiles.
    /// </summary>
    ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(
        CancellationToken cancellationToken);
}
