namespace McpServer.AgentRouter.Application.Abstractions;

/// <summary>
/// Resolves runtime paths for the AgentRouter host and infrastructure.
/// </summary>
public interface IAgentRouterRuntimePathResolver
{
    /// <summary>
    /// Temporarily overrides the content root used for relative resolution.
    /// </summary>
    IDisposable PushContentRoot(string contentRootPath);

    /// <summary>
    /// Resolves a configured path or falls back to the supplied default path.
    /// </summary>
    string ResolveConfiguredPath(string? configuredPath, string defaultPath);

    /// <summary>
    /// Resolves a path relative to the active content root.
    /// </summary>
    string ResolveRelativeToContentRoot(string path);
}
