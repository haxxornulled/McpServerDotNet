namespace McpServer.Application.Files.Commands;

public sealed record WriteFileTextCommand(
    string Path,
    string Content,
    string? Encoding = null,
    bool Overwrite = true);
