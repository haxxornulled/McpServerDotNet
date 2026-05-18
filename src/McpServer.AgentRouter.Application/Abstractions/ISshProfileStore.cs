using LanguageExt;
using McpServer.AgentRouter.Application.Ssh;

namespace McpServer.AgentRouter.Application.Abstractions;

public interface ISshProfileStore
{
    ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(
        CancellationToken cancellationToken);
}
