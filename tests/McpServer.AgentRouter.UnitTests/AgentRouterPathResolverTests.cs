using McpServer.AgentRouter.Application.Runtime;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AgentRouterPathResolverTests
{
    [Fact]
    public void ResolveConfiguredPath_UsesScopedContentRoot_ForRelativePath()
    {
        var resolver = new AgentRouterRuntimePathResolver();
        using var contentRoot = new TemporaryDirectory("agentrouter-pathresolver-root");
        using var scope = resolver.PushContentRoot(contentRoot.Path);

        var resolved = resolver.ResolveConfiguredPath(
            Path.Combine("..", "workspace", "artifacts"),
            "workspace");

        var expected = Path.GetFullPath(Path.Combine(contentRoot.Path, "..", "workspace", "artifacts"));
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveRelativeToContentRoot_UsesAppContextBaseDirectory_WhenScopeIsNotSet()
    {
        var resolver = new AgentRouterRuntimePathResolver();

        var resolved = resolver.ResolveRelativeToContentRoot("workspace");

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "workspace"), resolved);
    }

    [Fact]
    public void PushContentRoot_RestoresPreviousScope_WhenDisposed()
    {
        var resolver = new AgentRouterRuntimePathResolver();
        using var first = new TemporaryDirectory("agentrouter-pathresolver-first");
        using var second = new TemporaryDirectory("agentrouter-pathresolver-second");
        using var firstScope = resolver.PushContentRoot(first.Path);

        var firstResolved = resolver.ResolveRelativeToContentRoot("workspace");
        Assert.Equal(Path.Combine(first.Path, "workspace"), firstResolved);

        using (resolver.PushContentRoot(second.Path))
        {
            var secondResolved = resolver.ResolveRelativeToContentRoot("workspace");
            Assert.Equal(Path.Combine(second.Path, "workspace"), secondResolved);
        }

        var restoredResolved = resolver.ResolveRelativeToContentRoot("workspace");
        Assert.Equal(Path.Combine(first.Path, "workspace"), restoredResolved);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(string name)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
