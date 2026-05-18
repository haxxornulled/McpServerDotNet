namespace McpServer.AgentRouter.Application.Abstractions;

public interface IAgentRouterRuntimePathResolver
{
    IDisposable PushContentRoot(string contentRootPath);

    string ResolveConfiguredPath(string? configuredPath, string defaultPath);

    string ResolveRelativeToContentRoot(string path);
}
