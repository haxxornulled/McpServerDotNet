namespace McpServer.Application.Execution.Commands;

public sealed record RunProcessCommand(
    string Command,
    IEnumerable<string>? Arguments = null,
    string? WorkingDirectory = null,
    int TimeoutSeconds = 30,
    int MaxOutputChars = 12000);
