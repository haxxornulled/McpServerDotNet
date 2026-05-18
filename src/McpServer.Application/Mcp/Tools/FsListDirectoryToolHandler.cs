using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsListDirectoryToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsListDirectoryToolHandler> logger) : IToolHandler<FsListDirectoryRequest>
    {
        public string Name => "fs.list_directory";
        public string Description => "Lists the contents of a directory.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    path = new { type = "string" },
                    search_pattern = new { type = "string" }
                },
                required = new[] { "path" }
            });

        public IReadOnlyList<string> Validate(FsListDirectoryRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(FsListDirectoryRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .ListDirectoryAsync(new ListDirectoryCommand(request.Path, request.SearchPattern), ct)
                .ConfigureAwait(false);

            return result.Map(directoryResult =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                
                var entries = directoryResult.Entries.Select(e => new 
                {
                    name = e.Name,
                    is_directory = e.IsDirectory
                });
                
                var content = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                
                return new CallToolResult(
                [
                    new ContentItem("text", content)
                ], 
                StructuredContent: directoryResult);
            });
        }
    }
}
