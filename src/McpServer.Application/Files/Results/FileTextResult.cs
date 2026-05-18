namespace McpServer.Application.Files.Results
{
    public sealed record FileTextResult
    {
        public string Path { get; init; }
        public string Content { get; init; }

        public FileTextResult(string path, string content)
        {
            Path = path;
            Content = content;
        }
    }
}
