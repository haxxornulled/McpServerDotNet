namespace McpServer.Application.Files
{
    public sealed record DirectoryEntry
    {
        public string Name { get; init; }
        public bool IsDirectory { get; init; }

        public DirectoryEntry(string name, bool isDirectory)
        {
            Name = name;
            IsDirectory = isDirectory;
        }
    }
}
