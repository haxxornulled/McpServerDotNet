using McpServer.AgentRouter.Domain.Inference;
using McpServer.AgentRouter.Application.Ssh;

namespace McpServer.AgentRouter.Application.Runtime;

/// <summary>
/// Captures the application-owned runtime settings used by AgentRouter services.
/// </summary>
public sealed class AgentRouterRuntimeSettings
{
    /// <summary>
    /// Gets or sets the default model profile name.
    /// </summary>
    public required string DefaultProfile { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether cloud providers are allowed.
    /// </summary>
    public bool AllowCloudProviders { get; init; }

    /// <summary>
    /// Gets or sets the configured model profiles.
    /// </summary>
    public required IReadOnlyDictionary<string, ModelProfile> ModelProfiles { get; init; }

    /// <summary>
    /// Gets or sets run storage settings.
    /// </summary>
    public required AgentRunStorageRuntimeSettings RunStorage { get; init; }

    /// <summary>
    /// Gets or sets MCP server client settings.
    /// </summary>
    public required McpServerClientRuntimeSettings McpServer { get; init; }

    /// <summary>
    /// Gets or sets autonomous loop settings.
    /// </summary>
    public required AgentLoopRuntimeSettings AgentLoop { get; init; }

    /// <summary>
    /// Gets or sets MCP tool execution settings.
    /// </summary>
    public required McpToolExecutionRuntimeSettings ToolExecution { get; init; }

    /// <summary>
    /// Gets or sets shell execution settings.
    /// </summary>
    public required ShellExecutionRuntimeSettings ShellExecution { get; init; }

    /// <summary>
    /// Gets or sets SSH execution settings.
    /// </summary>
    public required SshExecutionRuntimeSettings SshExecution { get; init; }
}

/// <summary>
/// Configures managed local model runtime startup behavior.
/// </summary>
public sealed class LocalModelRuntimeStartupSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the runtime should be started if missing.
    /// </summary>
    public bool StartRuntimeIfMissing { get; init; }

    /// <summary>
    /// Gets or sets the executable path for the runtime.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Gets or sets the base URL of the runtime.
    /// </summary>
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Gets or sets the startup timeout in seconds.
    /// </summary>
    public int StartupTimeoutSeconds { get; init; }

    /// <summary>
    /// Gets or sets the polling interval in milliseconds.
    /// </summary>
    public int PollIntervalMilliseconds { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether missing models should be pulled.
    /// </summary>
    public bool PullMissingModels { get; init; }

    /// <summary>
    /// Gets or sets the required models.
    /// </summary>
    public required IReadOnlyList<string> RequiredModels { get; init; }
}

/// <summary>
/// Configures run storage behavior.
/// </summary>
public sealed class AgentRunStorageRuntimeSettings
{
    /// <summary>
    /// Gets or sets the storage root path.
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether artifact files should be written.
    /// </summary>
    public bool WriteArtifactFiles { get; init; }
}

/// <summary>
/// Configures the MCP server client used by AgentRouter.
/// </summary>
public sealed class McpServerClientRuntimeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the MCP server client is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets the executable path.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets the workspace root path.
    /// </summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether high-risk tools should be disabled.
    /// </summary>
    public bool DisableHighRiskTools { get; init; }

    /// <summary>
    /// Gets or sets the process environment variables.
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Environment { get; init; }
}

/// <summary>
/// Configures autonomous loop execution.
/// </summary>
public sealed class AgentLoopRuntimeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the autonomous loop is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of steps.
    /// </summary>
    public int MaxSteps { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of tool calls.
    /// </summary>
    public int MaxToolCalls { get; init; }

    /// <summary>
    /// Gets or sets the maximum runtime in seconds.
    /// </summary>
    public int MaxRuntimeSeconds { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether explicit capability allowlisting is required.
    /// </summary>
    public bool RequireExplicitAllowlist { get; init; }

    /// <summary>
    /// Gets or sets the allowed capabilities.
    /// </summary>
    public required IReadOnlyList<string> AllowedCapabilities { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether trace files should be written.
    /// </summary>
    public bool WriteTraceFiles { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether every step should be traced.
    /// </summary>
    public bool TraceEveryStep { get; init; }

    /// <summary>
    /// Gets or sets the trace root path.
    /// </summary>
    public required string TraceRootPath { get; init; }
}

/// <summary>
/// Configures MCP tool execution policy and runtime limits.
/// </summary>
public sealed class McpToolExecutionRuntimeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether MCP tool execution is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether explicit allowlisting is required.
    /// </summary>
    public bool RequireExplicitAllowlist { get; init; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets or sets the maximum output size in characters.
    /// </summary>
    public int MaxOutputChars { get; init; }

    /// <summary>
    /// Gets or sets the allowed tools.
    /// </summary>
    public required IReadOnlySet<string> AllowedTools { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether trace files should be written.
    /// </summary>
    public bool WriteTraceFiles { get; init; }

    /// <summary>
    /// Gets or sets the trace root path.
    /// </summary>
    public required string TraceRootPath { get; init; }
}

/// <summary>
/// Configures shell execution policy and runtime limits.
/// </summary>
public sealed class ShellExecutionRuntimeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether shell execution is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether explicit allowlisting is required.
    /// </summary>
    public bool RequireExplicitAllowlist { get; init; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets or sets the maximum output size in characters.
    /// </summary>
    public int MaxOutputChars { get; init; }

    /// <summary>
    /// Gets or sets the root directory for working directories.
    /// </summary>
    public required string WorkingDirectoryRoot { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether working directories outside the root are allowed.
    /// </summary>
    public bool AllowWorkingDirectoryOutsideRoot { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether inline shell interpreter commands are allowed.
    /// </summary>
    public bool AllowShellInterpreterInlineCommands { get; init; }

    /// <summary>
    /// Gets or sets the allowed commands.
    /// </summary>
    public required IReadOnlySet<string> AllowedCommands { get; init; }

    /// <summary>
    /// Gets or sets the denied commands.
    /// </summary>
    public required IReadOnlySet<string> DeniedCommands { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether trace files should be written.
    /// </summary>
    public bool WriteTraceFiles { get; init; }

    /// <summary>
    /// Gets or sets the trace root path.
    /// </summary>
    public required string TraceRootPath { get; init; }
}

/// <summary>
/// Configures SSH execution policy and runtime limits.
/// </summary>
public sealed class SshExecutionRuntimeSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether SSH execution is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether explicit profile allowlisting is required.
    /// </summary>
    public bool RequireExplicitProfileAllowlist { get; init; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// Gets or sets the maximum output size in characters.
    /// </summary>
    public int MaxOutputChars { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether unknown host keys are allowed.
    /// </summary>
    public bool AllowUnknownHostKeys { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether inline shell commands are allowed.
    /// </summary>
    public bool AllowShellInterpreterInlineCommands { get; init; }

    /// <summary>
    /// Gets or sets the allowed commands.
    /// </summary>
    public required IReadOnlySet<string> AllowedCommands { get; init; }

    /// <summary>
    /// Gets or sets the denied commands.
    /// </summary>
    public required IReadOnlySet<string> DeniedCommands { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether trace files should be written.
    /// </summary>
    public bool WriteTraceFiles { get; init; }

    /// <summary>
    /// Gets or sets the trace root path.
    /// </summary>
    public required string TraceRootPath { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether repository profiles should be loaded.
    /// </summary>
    public bool LoadRepoProfilesFile { get; init; }

    /// <summary>
    /// Gets or sets the repository profiles file path.
    /// </summary>
    public required string RepoProfilesFilePath { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether user profiles should be loaded.
    /// </summary>
    public bool LoadUserProfilesFile { get; init; }

    /// <summary>
    /// Gets or sets the user profiles file path.
    /// </summary>
    public required string UserProfilesFilePath { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether inline profiles are allowed.
    /// </summary>
    public bool AllowInlineProfiles { get; init; }

    /// <summary>
    /// Gets or sets the loaded SSH profiles.
    /// </summary>
    public required IReadOnlyDictionary<string, SshProfileDefinition> Profiles { get; init; }
}
