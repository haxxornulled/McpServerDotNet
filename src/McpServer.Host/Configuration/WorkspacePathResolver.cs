using McpServer.Domain.Workspace;

namespace McpServer.Host.Configuration;

public static class WorkspacePathResolver
{
    private const string DefaultWorkspaceRootEnvironmentVariable = "MCPSERVER_DEFAULT_WORKSPACE_ROOT";
    private const string WorkspaceRootEnvironmentVariable = "MCPSERVER__WORKSPACE__ROOTPATH";
    private const string WorkspaceRootAlternateEnvironmentVariable = "McpServer__Workspace__RootPath";

    public static WorkspaceResolution ResolveWorkspaceRoot(WorkspaceOptions options, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuredRootPath = options.RootPath;
        var hasEnvironmentOverride = HasWorkspaceRootEnvironmentOverride();

        if (ShouldUseApplicationDefaultWorkspace(configuredRootPath, hasEnvironmentOverride))
        {
            var defaultWorkspaceRoot = GetApplicationDefaultWorkspaceRoot(baseDirectory);

            return new WorkspaceResolution(
                workspaceRoot: defaultWorkspaceRoot,
                configuredRootPath: configuredRootPath,
                source: WorkspaceResolutionSource.ApplicationDefault,
                usedApplicationDefault: true);
        }

        var resolvedRoot = ResolveConfiguredPath(configuredRootPath, baseDirectory);

        return new WorkspaceResolution(
            workspaceRoot: resolvedRoot,
            configuredRootPath: configuredRootPath,
            source: hasEnvironmentOverride ? WorkspaceResolutionSource.Environment : WorkspaceResolutionSource.Configuration,
            usedApplicationDefault: false);
    }

    public static string[] BuildAllowedWorkspaceRoots(
        string workspaceRoot,
        IEnumerable<string>? allowedRoots,
        IEnumerable<string>? additionalAllowedRoots,
        string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        var roots = new List<string>
        {
            Path.GetFullPath(workspaceRoot)
        };

        if (allowedRoots is not null)
        {
            AppendResolvedRoots(roots, allowedRoots, baseDirectory);
        }

        if (additionalAllowedRoots is not null)
        {
            AppendResolvedRoots(roots, additionalAllowedRoots, baseDirectory);
        }

        return WorkspacePathState.NormalizeAllowedRoots(roots);
    }

    public static string GetApplicationDefaultWorkspaceRoot(string? baseDirectory = null)
    {
        var configuredDefault = Environment.GetEnvironmentVariable(DefaultWorkspaceRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredDefault))
        {
            return ResolveConfiguredPath(configuredDefault, baseDirectory);
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
        {
            return Path.GetFullPath(Path.Combine(localApplicationData, "McpServer", "workspace"));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.GetFullPath(Path.Combine(userProfile, ".mcpserver", "workspace"));
        }

        return Path.GetFullPath(Path.Combine(Path.GetTempPath(), "McpServer", "workspace"));
    }

    public static string ResolveConfiguredPath(string? path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return GetApplicationDefaultWorkspaceRoot(baseDirectory);
        }

        var trimmed = path.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(baseDirectory);

        return Path.GetFullPath(Path.Combine(rootDirectory, trimmed));
    }

    private static bool HasWorkspaceRootEnvironmentOverride()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WorkspaceRootEnvironmentVariable)) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WorkspaceRootAlternateEnvironmentVariable));
    }

    private static void AppendResolvedRoots(List<string> roots, IEnumerable<string> candidates, string? baseDirectory)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                roots.Add(ResolveConfiguredPath(candidate, baseDirectory));
            }
        }
    }

    private static bool ShouldUseApplicationDefaultWorkspace(
        string? configuredRootPath,
        bool hasEnvironmentOverride)
    {
        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            return true;
        }

        if (hasEnvironmentOverride)
        {
            return false;
        }

        var normalized = configuredRootPath.Trim().Replace('\\', '/');
        return normalized.Equals("./workspace", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("workspace", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class WorkspaceResolution
{
    public WorkspaceResolution(
        string workspaceRoot,
        string? configuredRootPath,
        WorkspaceResolutionSource source,
        bool usedApplicationDefault)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));
        }

        WorkspaceRoot = Path.GetFullPath(workspaceRoot);
        ConfiguredRootPath = configuredRootPath ?? string.Empty;
        Source = source;
        UsedApplicationDefault = usedApplicationDefault;
    }

    public string WorkspaceRoot { get; }

    public string ConfiguredRootPath { get; }

    public WorkspaceResolutionSource Source { get; }

    public bool UsedApplicationDefault { get; }
}

public enum WorkspaceResolutionSource
{
    ApplicationDefault = 0,
    Configuration = 1,
    Environment = 2
}
