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
    IReadOnlyCollection<string> AllowedRemotePathPrefixes,
    bool AllowSudoCommand = false)
{
    public SshCredentialSecret? PasswordSecret { get; init; }

    public string? PasswordVaultItemName { get; init; }

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
            AllowedRemotePathPrefixes: [],
            AllowSudoCommand: false)
    {
    }
}

public sealed record SshCredentialSecret
{
    public string Algorithm { get; init; } = "argon2id-aesgcm-v1";

    public string Salt { get; init; } = string.Empty;

    public string Nonce { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Ciphertext { get; init; } = string.Empty;

    public int Iterations { get; init; } = 3;

    public int MemorySizeKiB { get; init; } = 65536;

    public int DegreeOfParallelism { get; init; } = 1;
}
