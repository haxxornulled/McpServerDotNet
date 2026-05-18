namespace McpServer.Application.Files.Commands;

public sealed record ListDirectoryCommand(
    string Path,
    string? SearchPattern = null);
