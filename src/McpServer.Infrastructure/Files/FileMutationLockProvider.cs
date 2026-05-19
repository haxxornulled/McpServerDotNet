using System.Diagnostics;
using McpServer.Application.Abstractions.Files;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Files;

public sealed class FileMutationLockProvider : IFileMutationLockProvider
{
    private const int LockStripeCount = 256;
    private readonly SemaphoreSlim[] _locks = CreateLocks();
    private readonly ILogger<FileMutationLockProvider> _logger;

    public FileMutationLockProvider(ILogger<FileMutationLockProvider> logger)
    {
        _logger = logger;
    }

    public async ValueTask<IAsyncDisposable> AcquireAsync(string normalizedPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("Normalized path is required.", nameof(normalizedPath));
        }

        var gate = GetLock(normalizedPath);
        var started = Stopwatch.GetTimestamp();

        await gate.WaitAsync(ct).ConfigureAwait(false);

        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsedMs >= 25)
        {
            _logger.LogWarning(
                "Write lock wait detected for {NormalizedPath} after {ElapsedMs}ms",
                normalizedPath,
                elapsedMs);
        }

        return new Releaser(gate);
    }

    public async ValueTask<IAsyncDisposable> AcquireManyAsync(IEnumerable<string> normalizedPaths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(normalizedPaths);

        var ordered = NormalizeAndSort(normalizedPaths);

        if (ordered.Length == 0)
        {
            return new CompositeReleaser(Array.Empty<IAsyncDisposable>());
        }

        var releasers = new List<IAsyncDisposable>(ordered.Length);

        try
        {
            foreach (var path in ordered)
            {
                var releaser = await AcquireAsync(path, ct).ConfigureAwait(false);
                releasers.Add(releaser);
            }

            return new CompositeReleaser(releasers);
        }
        catch
        {
            for (var i = releasers.Count - 1; i >= 0; i--)
            {
                await releasers[i].DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    private static SemaphoreSlim[] CreateLocks()
    {
        var locks = new SemaphoreSlim[LockStripeCount];
        for (var i = 0; i < locks.Length; i++)
        {
            locks[i] = new SemaphoreSlim(1, 1);
        }

        return locks;
    }

    private SemaphoreSlim GetLock(string normalizedPath)
    {
        var hash = PathComparison.Comparer.GetHashCode(normalizedPath) & int.MaxValue;
        return _locks[hash % _locks.Length];
    }

    private static string[] NormalizeAndSort(IEnumerable<string> normalizedPaths)
    {
        var values = new List<string>();
        var seen = new System.Collections.Generic.HashSet<string>(PathComparison.Comparer);
        foreach (var path in normalizedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (seen.Add(path))
            {
                values.Add(path);
            }
        }

        values.Sort(PathComparison.Comparer);
        return values.ToArray();
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate)
        {
            _gate = gate;
        }

        public ValueTask DisposeAsync()
        {
            _gate.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CompositeReleaser : IAsyncDisposable
    {
        private readonly IReadOnlyList<IAsyncDisposable> _releasers;

        public CompositeReleaser(IReadOnlyList<IAsyncDisposable> releasers)
        {
            _releasers = releasers;
        }

        public async ValueTask DisposeAsync()
        {
            for (var i = _releasers.Count - 1; i >= 0; i--)
            {
                await _releasers[i].DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
