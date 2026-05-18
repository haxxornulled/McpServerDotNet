using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IResourceProvider
    {
        ValueTask<Fin<Unit>> AcquireAsync(string resourceId, TimeSpan timeout, CancellationToken ct);
        ValueTask<Fin<Unit>> ReleaseAsync(string resourceId, CancellationToken ct);
    }
}