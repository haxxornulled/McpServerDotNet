namespace McpServer.Infrastructure.Ssh;

public sealed record SshCredentialVaultEntry(
    string Name,
    string? Description,
    SshCredentialSecret Secret,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
