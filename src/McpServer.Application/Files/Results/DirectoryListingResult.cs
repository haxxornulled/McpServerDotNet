using McpServer.Application.Files;

namespace McpServer.Application.Files.Results
{
    public sealed record DirectoryListingResult
    {
        public string Path { get; init; }
        public IReadOnlyList<DirectoryEntry> Entries { get; init; }

        public DirectoryListingResult(string path, IReadOnlyList<DirectoryEntry> entries)
        {
            Path = path;
            Entries = entries;
        }
    }
}
