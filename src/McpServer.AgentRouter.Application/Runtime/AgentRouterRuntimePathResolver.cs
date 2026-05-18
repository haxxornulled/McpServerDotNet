using McpServer.AgentRouter.Application.Abstractions;

namespace McpServer.AgentRouter.Application.Runtime;

public sealed class AgentRouterRuntimePathResolver : IAgentRouterRuntimePathResolver
{
    private readonly object _sync = new();
    private string? _contentRootOverride;

    public IDisposable PushContentRoot(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new ArgumentException("Content root path is required.", nameof(contentRootPath));
        }

        var normalized = Path.GetFullPath(contentRootPath);
        string? previous;

        lock (_sync)
        {
            previous = _contentRootOverride;
            _contentRootOverride = normalized;
        }

        return new Scope(this, previous);
    }

    public string ResolveConfiguredPath(
        string? configuredPath,
        string defaultPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultPath
            : configuredPath.Trim();

        return ResolveRelativeToContentRoot(value);
    }

    public string ResolveRelativeToContentRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        string? basePath;
        lock (_sync)
        {
            basePath = _contentRootOverride;
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.GetFullPath(Path.Combine(basePath, expanded));
    }

    private sealed class Scope : IDisposable
    {
        private readonly AgentRouterRuntimePathResolver _owner;
        private readonly string? _previous;
        private bool _disposed;

        public Scope(
            AgentRouterRuntimePathResolver owner,
            string? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_owner._sync)
            {
                _owner._contentRootOverride = _previous;
            }
            _disposed = true;
        }
    }
}
