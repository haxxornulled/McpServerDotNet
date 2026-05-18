namespace McpServer.Application.Files.Commands;

public sealed record CreateDirectoryCommand
{
    public string Path { get; init; }

    public CreateDirectoryCommand(string path)
    {
        Path = path;
    }
}
