namespace McpServer.AgentRouter.Application.Ssh;

/// <summary>
/// Holds resolved SSH profiles and their source status.
/// </summary>
public sealed class SshProfileCatalog
{
    /// <summary>
    /// Gets or sets the profile map keyed by profile name.
    /// </summary>
    public IDictionary<string, SshProfileDefinition> Profiles { get; set; } =
        new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the source load status entries.
    /// </summary>
    public IList<SshProfileSourceStatus> Sources { get; set; } =
        new List<SshProfileSourceStatus>();
}

/// <summary>
/// Describes a resolved SSH profile definition.
/// </summary>
public sealed class SshProfileDefinition
{
    /// <summary>
    /// Gets or sets the SSH host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SSH port.
    /// </summary>
    public int Port { get; set; } = 22;

    /// <summary>
    /// Gets or sets the SSH username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SSH password vault item name.
    /// </summary>
    public string? PasswordVaultItemName { get; set; }

    /// <summary>
    /// Gets or sets the private key path.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the private key passphrase vault item name.
    /// </summary>
    public string? PrivateKeyPassphraseVaultItemName { get; set; }

    /// <summary>
    /// Gets or sets the default working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the expected host key hash.
    /// </summary>
    public string? HostKeySha256 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unknown host keys are accepted.
    /// </summary>
    public bool AcceptUnknownHostKey { get; set; }

    /// <summary>
    /// Gets or sets the allowed commands.
    /// </summary>
    public IList<string> AllowedCommands { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the denied commands.
    /// </summary>
    public IList<string> DeniedCommands { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the allowed remote path prefixes.
    /// </summary>
    public IList<string> AllowedRemotePathPrefixes { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether sudo is explicitly allowed for this profile.
    /// </summary>
    public bool AllowSudoCommand { get; set; }
}

/// <summary>
/// Captures the status of an SSH profile source during catalog loading.
/// </summary>
public sealed class SshProfileSourceStatus
{
    /// <summary>
    /// Gets or sets the source name.
    /// </summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the source path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the source is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the source file exists.
    /// </summary>
    public bool Exists { get; set; }

    /// <summary>
    /// Gets or sets the number of profiles loaded from the source.
    /// </summary>
    public int ProfileCount { get; set; }

    /// <summary>
    /// Gets or sets an optional status message.
    /// </summary>
    public string? Message { get; set; }
}
