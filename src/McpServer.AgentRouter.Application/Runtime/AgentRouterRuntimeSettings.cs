using McpServer.AgentRouter.Domain.Inference;
using McpServer.AgentRouter.Application.Ssh;

namespace McpServer.AgentRouter.Application.Runtime;

public sealed class AgentRouterRuntimeSettings
{
    public required string DefaultProfile { get; init; }

    public bool AllowCloudProviders { get; init; }

    public required IReadOnlyDictionary<string, ModelProfile> ModelProfiles { get; init; }

    public required AgentRunStorageRuntimeSettings RunStorage { get; init; }

    public required McpServerClientRuntimeSettings McpServer { get; init; }

    public required AgentLoopRuntimeSettings AgentLoop { get; init; }

    public required McpToolExecutionRuntimeSettings ToolExecution { get; init; }

    public required ShellExecutionRuntimeSettings ShellExecution { get; init; }

    public required SshExecutionRuntimeSettings SshExecution { get; init; }
}

public sealed class LocalModelRuntimeStartupSettings
{
    public bool StartRuntimeIfMissing { get; init; }

    public required string ExecutablePath { get; init; }

    public required string BaseUrl { get; init; }

    public int StartupTimeoutSeconds { get; init; }

    public int PollIntervalMilliseconds { get; init; }

    public bool PullMissingModels { get; init; }

    public required IReadOnlyList<string> RequiredModels { get; init; }
}

public sealed class AgentRunStorageRuntimeSettings
{
    public required string RootPath { get; init; }

    public bool WriteArtifactFiles { get; init; }
}

public sealed class McpServerClientRuntimeSettings
{
    public bool Enabled { get; init; }

    public required string ExecutablePath { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string WorkspaceRoot { get; init; }

    public int TimeoutSeconds { get; init; }

    public bool DisableHighRiskTools { get; init; }

    public required IReadOnlyDictionary<string, string?> Environment { get; init; }
}

public sealed class AgentLoopRuntimeSettings
{
    public bool Enabled { get; init; }

    public int MaxSteps { get; init; }

    public int MaxToolCalls { get; init; }

    public int MaxRuntimeSeconds { get; init; }

    public bool RequireExplicitAllowlist { get; init; }

    public required IReadOnlyList<string> AllowedCapabilities { get; init; }

    public bool WriteTraceFiles { get; init; }

    public bool TraceEveryStep { get; init; }

    public required string TraceRootPath { get; init; }
}

public sealed class McpToolExecutionRuntimeSettings
{
    public bool Enabled { get; init; }

    public bool RequireExplicitAllowlist { get; init; }

    public int TimeoutSeconds { get; init; }

    public int MaxOutputChars { get; init; }

    public required IReadOnlySet<string> AllowedTools { get; init; }

    public bool WriteTraceFiles { get; init; }

    public required string TraceRootPath { get; init; }
}

public sealed class ShellExecutionRuntimeSettings
{
    public bool Enabled { get; init; }

    public bool RequireExplicitAllowlist { get; init; }

    public int TimeoutSeconds { get; init; }

    public int MaxOutputChars { get; init; }

    public required string WorkingDirectoryRoot { get; init; }

    public bool AllowWorkingDirectoryOutsideRoot { get; init; }

    public bool AllowShellInterpreterInlineCommands { get; init; }

    public required IReadOnlySet<string> AllowedCommands { get; init; }

    public required IReadOnlySet<string> DeniedCommands { get; init; }

    public bool WriteTraceFiles { get; init; }

    public required string TraceRootPath { get; init; }
}

public sealed class SshExecutionRuntimeSettings
{
    public bool Enabled { get; init; }

    public bool RequireExplicitProfileAllowlist { get; init; }

    public int TimeoutSeconds { get; init; }

    public int MaxOutputChars { get; init; }

    public bool AllowUnknownHostKeys { get; init; }

    public bool AllowShellInterpreterInlineCommands { get; init; }

    public required IReadOnlySet<string> AllowedCommands { get; init; }

    public required IReadOnlySet<string> DeniedCommands { get; init; }

    public bool WriteTraceFiles { get; init; }

    public required string TraceRootPath { get; init; }

    public bool LoadRepoProfilesFile { get; init; }

    public required string RepoProfilesFilePath { get; init; }

    public bool LoadUserProfilesFile { get; init; }

    public required string UserProfilesFilePath { get; init; }

    public bool AllowInlineProfiles { get; init; }

    public required IReadOnlyDictionary<string, SshProfileDefinition> Profiles { get; init; }
}
