namespace McpServer.Application.Files.Commands;

public sealed record DeletePathCommand(
    string Path,
    bool Recursive = false,
    string? Confirmation = null);
