namespace McpServer.UnitTests.Host;

internal sealed class WorkspaceEnvironmentScope : IDisposable
{
    private const string DefaultWorkspaceRootEnvironmentVariable = "MCPSERVER_DEFAULT_WORKSPACE_ROOT";
    private const string WorkspaceRootEnvironmentVariable = "MCPSERVER__WORKSPACE__ROOTPATH";
    private const string WorkspaceRootAlternateEnvironmentVariable = "McpServer__Workspace__RootPath";

    private static readonly string[] WorkspaceRootEnvironmentVariableNames =
        OperatingSystem.IsWindows()
            ? [WorkspaceRootEnvironmentVariable]
            : [WorkspaceRootEnvironmentVariable, WorkspaceRootAlternateEnvironmentVariable];

    private readonly string? _previousDefaultWorkspaceRoot;
    private readonly Dictionary<string, string?> _previousWorkspaceRoots;

    public WorkspaceEnvironmentScope(
        string? defaultWorkspaceRoot,
        string? workspaceRoot)
    {
        _previousDefaultWorkspaceRoot = Environment.GetEnvironmentVariable(DefaultWorkspaceRootEnvironmentVariable);
        _previousWorkspaceRoots = WorkspaceRootEnvironmentVariableNames.ToDictionary(
            static variableName => variableName,
            static variableName => Environment.GetEnvironmentVariable(variableName),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        Environment.SetEnvironmentVariable(DefaultWorkspaceRootEnvironmentVariable, defaultWorkspaceRoot);
        ClearWorkspaceRootEnvironmentVariables();

        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            Environment.SetEnvironmentVariable(WorkspaceRootEnvironmentVariable, workspaceRoot);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DefaultWorkspaceRootEnvironmentVariable, _previousDefaultWorkspaceRoot);
        ClearWorkspaceRootEnvironmentVariables();

        foreach (var previousWorkspaceRoot in _previousWorkspaceRoots)
        {
            Environment.SetEnvironmentVariable(previousWorkspaceRoot.Key, previousWorkspaceRoot.Value);
        }
    }

    private static void ClearWorkspaceRootEnvironmentVariables()
    {
        foreach (var variableName in WorkspaceRootEnvironmentVariableNames)
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }
}
