using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsWriteTextToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsWriteTextToolHandler> logger) : IToolHandler<FsWriteTextRequest>
    {
        public string Name => "fs.write_text";
        public string Description => "Writes text to a file.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    path = new { type = "string" },
                    content = new { type = "string" },
                    encoding = new { type = "string" },
                    overwrite = new { type = "boolean", @default = true }
                },
                required = new[] { "path", "content" }
            });

        public IReadOnlyList<string> Validate(FsWriteTextRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(FsWriteTextRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .WriteTextAsync(new WriteFileTextCommand(request.Path, request.Content, request.Encoding, request.Overwrite), ct)
                .ConfigureAwait(false);

            return result.Map(fileResult =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                return new CallToolResult(
                [
                    new ContentItem("text", $"Successfully wrote to file: {request.Path}")
                ], 
                StructuredContent: fileResult);
            });
        }
    }
}
