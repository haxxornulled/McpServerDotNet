using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsCopyPathToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsCopyPathToolHandler> logger) : IToolHandler<FsCopyPathRequest>
    {
        public string Name => "fs.copy_path";
        public string Description => "Copies a file or directory.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    source_path = new { type = "string" },
                    destination_path = new { type = "string" },
                    overwrite = new { type = "boolean" },
                    recursive = new { type = "boolean" }
                },
                required = new[] { "source_path", "destination_path" }
            });

        public IReadOnlyList<string> Validate(FsCopyPathRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.SourcePath, "source_path");
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.DestinationPath, "destination_path");
            ToolRequestValidation.RequireNotRootLikePath(errors, request.DestinationPath, "destination_path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(FsCopyPathRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .CopyPathAsync(new CopyPathCommand(request.SourcePath, request.DestinationPath, request.Overwrite, request.Recursive), ct)
                .ConfigureAwait(false);

            return result.Map(_ =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                var message = $"Successfully copied path from {request.SourcePath} to {request.DestinationPath}";
                return new CallToolResult(
                [
                    new ContentItem("text", message)
                ],
                StructuredContent: new ToolOperationResultDto(Name, Success: true, message, SourcePath: request.SourcePath, DestinationPath: request.DestinationPath));
            });
        }
    }
}
