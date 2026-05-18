namespace McpServer.Application.Files.Commands;

public sealed record MovePathCommand(
    string SourcePath,
    string DestinationPath,
    bool Overwrite = false);
