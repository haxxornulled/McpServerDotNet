namespace McpServer.Application.Files.Commands;

public sealed record AppendFileTextCommand(
    string Path,
    string Content,
    string? Encoding = null,
    bool Flush = true);
