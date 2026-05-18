namespace McpServer.AgentRouter.Application.Ssh;

public sealed class SshProfileCatalog
{
    public IDictionary<string, SshProfileDefinition> Profiles { get; set; } =
        new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

    public IList<SshProfileSourceStatus> Sources { get; set; } =
        new List<SshProfileSourceStatus>();
}

public sealed class SshProfileDefinition
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

public sealed class SshProfileSourceStatus
{
    public string SourceName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool Exists { get; set; }

    public int ProfileCount { get; set; }

    public string? Message { get; set; }
}
