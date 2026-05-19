using McpServer.Infrastructure.Ssh;

namespace McpServer.Host.Configuration;

public sealed class McpServerOptions
{
    public const string SectionName = "McpServer";

    public WorkspaceOptions Workspace { get; init; } = new();
    public ShellOptions Shell { get; init; } = new();
    public WebAccessOptions WebAccess { get; init; } = new();
    public SshOptions Ssh { get; init; } = new();
    public OllamaOptions Ollama { get; init; } = new();
}

public sealed class WorkspaceOptions
{
    public string RootPath { get; init; } = string.Empty;

    // Preferred configuration key. Maps to MCPSERVER__WORKSPACE__ALLOWEDROOTS__0, etc.
    public string[] AllowedRoots { get; init; } = [];

    // Backward-compatible alias for the existing appsettings.json shape.
    public string[] AdditionalAllowedRoots { get; init; } = [];

    public bool AllowRuntimeWorkspaceOpen { get; init; } = true;
}

public sealed class ShellOptions
{
    public bool Enabled { get; init; }
    public bool AllowShellFallback { get; init; }
    public string[] AllowedCommands { get; init; } = [];
    public string[] DeniedCommands { get; init; } =
    [
        "bash",
        "bitsadmin",
        "certutil",
        "cmd",
        "curl",
        "del",
        "erase",
        "format",
        "mkfs",
        "mshta",
        "net",
        "netsh",
        "reg",
        "regsvr32",
        "rm",
        "rmdir",
        "rundll32",
        "schtasks",
        "sh",
        "shutdown",
        "ssh",
        "wscript",
        "zsh"
    ];
    public int MaxTimeoutSeconds { get; init; } = 120;
    public int MaxOutputChars { get; init; } = 12000;
}

public sealed class WebAccessOptions
{
    public bool Enabled { get; init; }
    public string[] AllowedHosts { get; init; } = [];
    public bool AllowLocalLoopbackHosts { get; init; }
    public string SearchBaseUrl { get; init; } = "https://duckduckgo.com/html/?q=";
}

public sealed class SshOptions
{
    public bool Enabled { get; init; }
    public SshProfileOptions[] Profiles { get; init; } = [];

    public bool LoadRepoProfilesFile { get; init; } = true;

    public string RepoProfilesFilePath { get; init; } = Path.Combine("..", "..", "..", "..", "..", "config", "mcpserver", "ssh-profiles.local.json");

    public bool LoadUserProfilesFile { get; init; } = true;

    public string UserProfilesFilePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McpServer",
        "ssh-profiles.json");

    public bool AllowInlineProfiles { get; init; }

    public string VaultPath { get; init; } = Path.Combine("..", "..", "..", "..", "..", "config", "mcpserver", "ssh-vault.local.json");

    public string VaultKeyPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "McpServer",
        "ssh-vault.key");

    public bool UseTestBackend { get; init; }
    public string TestBackendRootPath { get; init; } = string.Empty;
}

public sealed class SshProfileOptions
{
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 22;
    public string Username { get; init; } = string.Empty;
    public string? PrivateKeyPath { get; init; }
    public string? PasswordVaultItemName { get; init; }
    public string? PrivateKeyPassphraseVaultItemName { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? HostKeySha256 { get; init; }
    public bool AcceptUnknownHostKey { get; init; }
    public string[] AllowedCommands { get; init; } = [];
    public string[] DeniedCommands { get; init; } =
    [
        "bash",
        "curl",
        "nc",
        "netcat",
        "rm",
        "scp",
        "sh",
        "ssh",
        "wget",
        "zsh"
    ];
    public string[] AllowedRemotePathPrefixes { get; init; } = [];
    public bool AllowSudoCommand { get; init; }
    public bool AllowAllCommands { get; init; }
}


public sealed class OllamaOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";
    public string DefaultModel { get; init; } = "qwen25-coder-14b-64k";
    public string[] AllowedModels { get; init; } = [];
    public int TimeoutSeconds { get; init; } = 120;
    public int MaxTimeoutSeconds { get; init; } = 600;
    public int MaxPromptChars { get; init; } = 120000;
    public int MaxOutputChars { get; init; } = 32000;
    public int ContextLength { get; init; } = 131072;
    public int? NumPredict { get; init; } = 32000;
    public double Temperature { get; init; } = 0.15d;
    public bool AllowNonLoopbackBaseUrl { get; init; }
}
