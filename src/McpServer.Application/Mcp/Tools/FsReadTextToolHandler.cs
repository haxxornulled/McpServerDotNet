using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsReadTextToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsReadTextToolHandler> logger) : IToolHandler<FsReadTextRequest>
    {
        public string Name => "fs.read_text";
        public string Description => "Reads the contents of a text file.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    path = new { type = "string" },
                    encoding = new { type = "string" }
                },
                required = new[] { "path" }
            });

        public IReadOnlyList<string> Validate(FsReadTextRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(FsReadTextRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .ReadTextAsync(new ReadFileTextCommand(request.Path, request.Encoding), ct)
                .ConfigureAwait(false);

            return result.Map(fileResult =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                return new CallToolResult(
                [
                    new ContentItem("text", fileResult.Content)
                ], 
                StructuredContent: fileResult);
            });
        }
    }
}
