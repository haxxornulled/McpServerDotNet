namespace McpServer.AgentRouter.Host.Configuration;

public sealed class AgentRouterOptions
{
    public const string SectionName = "AgentRouter";

    public string BindUrl { get; set; } = "http://127.0.0.1:5177";

    public bool RequireApiKey { get; set; }

    public string? ApiKey { get; set; }

    public string DefaultProfile { get; set; } = "local-code";

    public bool AllowCloudProviders { get; set; }

    public AgentRunStorageOptions RunStorage { get; set; } = new();

    public AgentRouterStartupOptions Startup { get; set; } = new();

    public McpServerClientOptions McpServer { get; set; } = new();

    public McpToolExecutionOptions ToolExecution { get; set; } = new();

    public ShellExecutionOptions ShellExecution { get; set; } = new();

    public SshExecutionOptions SshExecution { get; set; } = new();

    public AgentLoopOptions AgentLoop { get; set; } = new();

    public AgentLoopOptions AutonomousLoop
    {
        get => AgentLoop;
        set => AgentLoop = value ?? new AgentLoopOptions();
    }

    public IDictionary<string, ModelProfileOptions> ModelProfiles { get; set; } =
        new Dictionary<string, ModelProfileOptions>(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentRunStorageOptions
{
    public string RootPath { get; set; } = Path.Combine("workspace", "artifacts", "agent-runs");

    public bool WriteArtifactFiles { get; set; } = true;
}

public sealed class AgentRouterStartupOptions
{
    public bool Enabled { get; set; } = true;

    public bool EnsureRunStorageRoot { get; set; } = true;

    public bool EnsureOllama { get; set; } = true;

    public bool StartOllamaIfMissing { get; set; } = true;

    public bool StopManagedOllamaOnShutdown { get; set; }

    public string OllamaExecutablePath { get; set; } = "ollama";

    public string OllamaBaseUrl { get; set; } = "http://127.0.0.1:11434";

    public int StartupTimeoutSeconds { get; set; } = 60;

    public int PollIntervalMilliseconds { get; set; } = 500;

    public bool PullMissingModels { get; set; }

    public IList<string> RequiredModels { get; set; } = new List<string>
    {
        "qwen2.5-coder:14b"
    };

    public bool VerifyMcpToolCatalogAfterStart { get; set; } = true;

    public bool FailFastOnStartupFailure { get; set; } = true;
}

public sealed class McpServerClientOptions
{
    public bool Enabled { get; set; } = true;

    public string ExecutablePath { get; set; } = Path.Combine("..", "McpServer.Host", "bin", "Release", "net10.0", GetDefaultExecutableName());

    public string WorkingDirectory { get; set; } = Path.Combine("..", "..");

    public string WorkspaceRoot { get; set; } = Path.Combine("..", "..", "workspace");

    public int TimeoutSeconds { get; set; } = 20;

    public bool DisableHighRiskTools { get; set; } = true;

    public IDictionary<string, string?> Environment { get; set; } =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private static string GetDefaultExecutableName()
    {
        return OperatingSystem.IsWindows()
            ? "McpServer.Host.exe"
            : "McpServer.Host";
    }
}

public sealed class McpToolExecutionOptions
{
    public bool Enabled { get; set; } = true;

    public bool RequireExplicitAllowlist { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 20;

    public int MaxOutputChars { get; set; } = 200000;

    public bool WriteTraceFiles { get; set; } = true;

    public string TraceRootPath { get; set; } = Path.Combine("workspace", "artifacts", "mcp-tool-calls");

    public IList<string> AllowedTools { get; set; } = new List<string>
    {
        "activity.schemas.list",
        "fs.get_metadata",
        "fs.list_directory"
    };
}

public sealed class ShellExecutionOptions
{
    public bool Enabled { get; set; } = true;

    public bool RequireExplicitAllowlist { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxOutputChars { get; set; } = 200000;

    public bool WriteTraceFiles { get; set; } = true;

    public string TraceRootPath { get; set; } = Path.Combine("workspace", "artifacts", "shell-exec");

    public string WorkingDirectoryRoot { get; set; } = "workspace";

    /// <summary>
    /// Compatibility alias for operator-facing configuration.
    /// Prefer WorkingDirectoryRoot in code, but allow
    /// AgentRouter:ShellExecution:WorkspaceRoot in launchSettings/appsettings.
    /// </summary>
    public string WorkspaceRoot
    {
        get => WorkingDirectoryRoot;
        set => WorkingDirectoryRoot = string.IsNullOrWhiteSpace(value) ? "workspace" : value;
    }

    public bool AllowWorkingDirectoryOutsideRoot { get; set; }

    public bool AllowShellInterpreterInlineCommands { get; set; }

    public IList<string> AllowedCommands { get; set; } = new List<string>
    {
        "dotnet",
        "git",
        "pwsh",
        "bash"
    };

    public IList<string> DeniedCommands { get; set; } = new List<string>
    {
        "rm",
        "del",
        "erase",
        "rmdir",
        "shutdown",
        "reboot",
        "mkfs",
        "format",
        "diskpart",
        "sudo",
        "su"
    };
}

public sealed class SshExecutionOptions
{
    public bool Enabled { get; set; } = true;

    public bool RequireExplicitProfileAllowlist { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxOutputChars { get; set; } = 200000;

    public bool WriteTraceFiles { get; set; } = true;

    public string TraceRootPath { get; set; } = Path.Combine("workspace", "artifacts", "ssh-exec");

    public bool AllowUnknownHostKeys { get; set; }

    public bool AllowShellInterpreterInlineCommands { get; set; }

    /// <summary>
    /// Repo-local operator profile file. This file is intended for local/lab
    /// machine metadata and should normally be ignored by git.
    /// </summary>
    public string RepoProfilesFilePath { get; set; } = Path.Combine("..", "..", "config", "agentrouter", "ssh-profiles.local.json");

    /// <summary>
    /// User-level operator profile file. If empty, the infrastructure profile
    /// store resolves it to %LOCALAPPDATA%/McpServer/AgentRouter/ssh-profiles.json
    /// on Windows, or the platform equivalent local application-data path.
    /// </summary>
    public string UserProfilesFilePath { get; set; } = string.Empty;

    public bool LoadRepoProfilesFile { get; set; } = true;

    public bool LoadUserProfilesFile { get; set; } = true;

    /// <summary>
    /// Legacy compatibility only. Keep false for normal operator runtime so
    /// appsettings never becomes the SSH profile database.
    /// </summary>
    public bool AllowInlineProfiles { get; set; }

    public IList<string> AllowedCommands { get; set; } = new List<string>
    {
        "pwd",
        "whoami",
        "hostname",
        "uname",
        "uptime"
    };

    public IList<string> DeniedCommands { get; set; } = new List<string>
    {
        "rm",
        "del",
        "erase",
        "rmdir",
        "shutdown",
        "reboot",
        "mkfs",
        "format",
        "diskpart",
        "sudo",
        "su"
    };

    public IDictionary<string, SshProfileOptions> Profiles { get; set; } =
        new Dictionary<string, SshProfileOptions>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SshProfileOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string? PasswordEnvironmentVariable { get; set; }

    public string? PrivateKeyPath { get; set; }

    public string? PrivateKeyPassphraseEnvironmentVariable { get; set; }

    public string? WorkingDirectory { get; set; }

    public string? HostKeySha256 { get; set; }

    public bool AcceptUnknownHostKey { get; set; }

    public IList<string> AllowedCommands { get; set; } = new List<string>();

    public IList<string> DeniedCommands { get; set; } = new List<string>();

    public IList<string> AllowedRemotePathPrefixes { get; set; } = new List<string>();
}

public class AgentLoopOptions
{
    public bool Enabled { get; set; } = true;

    public int MaxSteps { get; set; } = 8;

    public int MaxToolCalls { get; set; } = 20;

    public int MaxRuntimeSeconds { get; set; } = 300;

    public int MaxOutputChars { get; set; } = 200000;

    public bool TraceEveryStep { get; set; } = true;

    public bool WriteTraceFiles { get; set; } = true;

    public string TraceRootPath { get; set; } = Path.Combine("workspace", "artifacts", "agent-loops");

    public bool NoSilentRetries { get; set; } = true;

    public bool RequireExplicitAllowlist { get; set; } = true;

    public IList<string> AllowedCapabilities { get; set; } = new List<string>
    {
        "mcp.tools.call",
        "shell.exec",
        "ssh.exec"
    };

    public IList<string> AllowedTools
    {
        get => AllowedCapabilities;
        set => AllowedCapabilities = value ?? new List<string>();
    }
}

public sealed class AutonomousLoopOptions : AgentLoopOptions
{
}

public sealed class ModelProfileOptions
{
    public string Provider { get; set; } = "Ollama";

    public string Model { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public int ContextLength { get; set; } = 65536;

    public int MaxOutputTokens { get; set; } = 12000;

    public double Temperature { get; set; } = 0.15d;

    public bool AllowCloudProvider { get; set; }

    public bool AllowNonLoopbackBaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 600;
}
