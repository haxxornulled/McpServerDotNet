using McpServer.Host.Configuration;
using Xunit;

namespace McpServer.UnitTests.Host;

[Collection("Environment mutating tests")]
public sealed class WorkspacePathResolverExtendedTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void ResolveConfiguredPath_Should_Resolve_Relative_Path_From_ProvidedBaseDirectory()
    {
        using var baseDirectory = new TemporaryDirectory();

        var resolved = WorkspacePathResolver.ResolveConfiguredPath("repo", baseDirectory.Path);

        Assert.Equal(Path.Combine(baseDirectory.Path, "repo"), resolved);
    }

    [Fact]
    public void ResolveWorkspaceRoot_Should_Treat_Workspace_Without_DotSlash_As_ApplicationDefault()
    {
        lock (EnvironmentLock)
        {
            var defaultWorkspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
            using var environment = new WorkspaceEnvironmentScope(
                defaultWorkspaceRoot: defaultWorkspace,
                workspaceRoot: null);

            var result = WorkspacePathResolver.ResolveWorkspaceRoot(new WorkspaceOptions
            {
                RootPath = "workspace"
            });

            Assert.Equal(Path.GetFullPath(defaultWorkspace), result.WorkspaceRoot);
            Assert.Equal(WorkspaceResolutionSource.ApplicationDefault, result.Source);
            Assert.True(result.UsedApplicationDefault);
        }
    }

    [Fact]
    public void ResolveWorkspaceRoot_Should_Not_Treat_Legacy_Workspace_As_Default_When_EnvironmentOverride_Is_Set()
    {
        lock (EnvironmentLock)
        {
            using var baseDirectory = new TemporaryDirectory();
            var envWorkspace = Path.Combine(baseDirectory.Path, "env-workspace");
            using var environment = new WorkspaceEnvironmentScope(
                defaultWorkspaceRoot: null,
                workspaceRoot: envWorkspace);

            var result = WorkspacePathResolver.ResolveWorkspaceRoot(new WorkspaceOptions
            {
                RootPath = "./workspace"
            }, baseDirectory.Path);

            Assert.Equal(Path.Combine(baseDirectory.Path, "workspace"), result.WorkspaceRoot);
            Assert.Equal(WorkspaceResolutionSource.Environment, result.Source);
            Assert.False(result.UsedApplicationDefault);
        }
    }

    [Fact]
    public void BuildAllowedWorkspaceRoots_Should_Include_AdditionalAllowedRoots()
    {
        using var baseDirectory = new TemporaryDirectory();
        var workspace = Path.Combine(baseDirectory.Path, "workspace");
        var additional = "../other";

        var roots = WorkspacePathResolver.BuildAllowedWorkspaceRoots(
            workspace,
            Array.Empty<string>(),
            [additional],
            baseDirectory.Path);

        Assert.Contains(Path.GetFullPath(workspace), roots);
        Assert.Contains(Path.GetFullPath(Path.Combine(baseDirectory.Path, additional)), roots);
    }

    [Fact]
    public void BuildAllowedWorkspaceRoots_Should_Trim_Trailing_Separators()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace") + Path.DirectorySeparatorChar;

        var roots = WorkspacePathResolver.BuildAllowedWorkspaceRoots(
            workspace,
            Array.Empty<string>(),
            Array.Empty<string>());

        Assert.Single(roots);
        Assert.False(roots[0].EndsWith(Path.DirectorySeparatorChar));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcpserver-workspace-resolver-extended-tests", Guid.NewGuid().ToString("N")));
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
