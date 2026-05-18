using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsAppendTextToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsAppendTextToolHandler> logger) : IToolHandler<AppendFileTextRequest>
    {
        public string Name => "fs.append_text";
        public string Description => "Appends text to a file.";

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
                    flush = new { type = "boolean" }
                },
                required = new[] { "path", "content" }
            });

        public IReadOnlyList<string> Validate(AppendFileTextRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(AppendFileTextRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .AppendTextAsync(new AppendFileTextCommand(request.Path, request.Content, request.Encoding, request.Flush), ct)
                .ConfigureAwait(false);

            return result.Map(fileResult =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                return new CallToolResult(
                [
                    new ContentItem("text", $"Successfully appended to file: {request.Path}")
                ], 
                StructuredContent: fileResult);
            });
        }
    }
}
