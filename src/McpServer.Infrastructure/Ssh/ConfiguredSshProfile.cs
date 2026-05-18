namespace McpServer.Infrastructure.Ssh;

public sealed record ConfiguredSshProfile(
    string Name,
    string Host,
    int Port,
    string Username,
    string? PasswordEnvironmentVariable,
    string? PrivateKeyPath,
    string? PrivateKeyPassphraseEnvironmentVariable,
    string? WorkingDirectory,
    string? HostKeySha256,
    bool AcceptUnknownHostKey,
    IReadOnlyCollection<string> AllowedCommands,
    IReadOnlyCollection<string> DeniedCommands,
    IReadOnlyCollection<string> AllowedRemotePathPrefixes)
{
    public ConfiguredSshProfile(
        string Name,
        string Host,
        int Port,
        string Username,
        string? PasswordEnvironmentVariable,
        string? PrivateKeyPath,
        string? PrivateKeyPassphraseEnvironmentVariable,
        string? WorkingDirectory,
        string? HostKeySha256,
        bool AcceptUnknownHostKey)
        : this(
            Name: Name,
            Host: Host,
            Port: Port,
            Username: Username,
            PasswordEnvironmentVariable: PasswordEnvironmentVariable,
            PrivateKeyPath: PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable: PrivateKeyPassphraseEnvironmentVariable,
            WorkingDirectory: WorkingDirectory,
            HostKeySha256: HostKeySha256,
            AcceptUnknownHostKey: AcceptUnknownHostKey,
            AllowedCommands: [],
            DeniedCommands: [],
            AllowedRemotePathPrefixes: [])
    {
    }
}
