namespace McpServer.Application.Files.Commands;

public sealed record CopyPathCommand(
    string SourcePath,
    string DestinationPath,
    bool Overwrite = false,
    bool Recursive = false);
