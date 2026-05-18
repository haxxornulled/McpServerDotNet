using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IDistributedLockService
    {
        ValueTask<Fin<IDisposable>> AcquireAsync(string resource, TimeSpan timeout, CancellationToken ct);
    }
}