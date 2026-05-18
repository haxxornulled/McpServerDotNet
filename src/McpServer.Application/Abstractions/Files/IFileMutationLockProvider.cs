namespace McpServer.Application.Abstractions.Files;

public interface IFileMutationLockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(string normalizedPath, CancellationToken ct);
    ValueTask<IAsyncDisposable> AcquireManyAsync(IEnumerable<string> normalizedPaths, CancellationToken ct);
}
