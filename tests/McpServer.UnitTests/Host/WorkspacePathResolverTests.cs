using McpServer.Host.Configuration;
using Xunit;

namespace McpServer.UnitTests.Host;

[Collection("Environment mutating tests")]
public sealed class WorkspacePathResolverTests
{
    private static readonly object EnvironmentLock = new();

    [Fact]
    public void ResolveWorkspaceRoot_UsesApplicationDefault_WhenRootPathIsMissing()
    {
        lock (EnvironmentLock)
        {
            var defaultWorkspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
            using var environment = new WorkspaceEnvironmentScope(
                defaultWorkspaceRoot: defaultWorkspace,
                workspaceRoot: null);

            var result = WorkspacePathResolver.ResolveWorkspaceRoot(new WorkspaceOptions());

            Assert.Equal(Path.GetFullPath(defaultWorkspace), result.WorkspaceRoot);
            Assert.Equal(WorkspaceResolutionSource.ApplicationDefault, result.Source);
            Assert.True(result.UsedApplicationDefault);
        }
    }

    [Fact]
    public void ResolveWorkspaceRoot_TreatsLegacyWorkspacePlaceholderAsApplicationDefault_WhenEnvironmentIsNotSet()
    {
        lock (EnvironmentLock)
        {
            var defaultWorkspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
            using var environment = new WorkspaceEnvironmentScope(
                defaultWorkspaceRoot: defaultWorkspace,
                workspaceRoot: null);

            var options = new WorkspaceOptions
            {
                RootPath = "./workspace"
            };

            var result = WorkspacePathResolver.ResolveWorkspaceRoot(options);

            Assert.Equal(Path.GetFullPath(defaultWorkspace), result.WorkspaceRoot);
            Assert.Equal(WorkspaceResolutionSource.ApplicationDefault, result.Source);
            Assert.True(result.UsedApplicationDefault);
        }
    }

    [Fact]
    public void ResolveWorkspaceRoot_HonorsExplicitEnvironmentOverride()
    {
        lock (EnvironmentLock)
        {
            var configuredWorkspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "repo");
            using var environment = new WorkspaceEnvironmentScope(
                defaultWorkspaceRoot: null,
                workspaceRoot: configuredWorkspace);

            var options = new WorkspaceOptions
            {
                RootPath = configuredWorkspace
            };

            var result = WorkspacePathResolver.ResolveWorkspaceRoot(options);

            Assert.Equal(Path.GetFullPath(configuredWorkspace), result.WorkspaceRoot);
            Assert.Equal(WorkspaceResolutionSource.Environment, result.Source);
            Assert.False(result.UsedApplicationDefault);
        }
    }

    [Fact]
    public void BuildAllowedWorkspaceRoots_IncludesWorkspaceAndDistinctConfiguredRoots()
    {
        var workspace = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "workspace");
        var additional = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "repo");

        var roots = WorkspacePathResolver.BuildAllowedWorkspaceRoots(
            workspace,
            new[] { additional, additional },
            Array.Empty<string>());

        Assert.Equal(2, roots.Length);
        Assert.Contains(Path.GetFullPath(workspace), roots);
        Assert.Contains(Path.GetFullPath(additional), roots);
    }
}
