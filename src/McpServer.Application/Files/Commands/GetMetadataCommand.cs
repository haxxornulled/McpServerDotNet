namespace McpServer.Application.Files.Commands;

public sealed record GetMetadataCommand
{
    public string Path { get; init; }

    public GetMetadataCommand(string path)
    {
        Path = path;
    }
}
