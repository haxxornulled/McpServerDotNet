namespace McpServer.Application.Ssh.Commands;

public sealed record ExecuteSshCommand(
    string Profile,
    string Command,
    string? WorkingDirectory = null,
    string[]? Args = null,
    int TimeoutSeconds = 60,
    int MaxOutputChars = 12000);
