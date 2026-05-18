namespace McpServer.Application.Files.Results
{
    public record FileMetadataResult(
        string Path,
        bool Exists,
        bool IsDirectory,
        long? Size,
        DateTime CreationTime,
        DateTime LastWriteTime,
        string Attributes);
}