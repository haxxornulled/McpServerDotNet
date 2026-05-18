namespace McpServer.Application.Files.Commands;

public sealed record ReadFileTextCommand(
    string Path,
    string? Encoding = null);
