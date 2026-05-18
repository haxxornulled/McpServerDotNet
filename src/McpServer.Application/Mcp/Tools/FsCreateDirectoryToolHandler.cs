using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsCreateDirectoryToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsCreateDirectoryToolHandler> logger) : IToolHandler<CreateDirectoryRequest>
    {
        public string Name => "fs.create_directory";
        public string Description => "Creates a directory.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    path = new { type = "string" }
                },
                required = new[] { "path" }
            });

        public IReadOnlyList<string> Validate(CreateDirectoryRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            ToolRequestValidation.RequireNotRootLikePath(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(CreateDirectoryRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .CreateDirectoryAsync(new CreateDirectoryCommand(request.Path), ct)
                .ConfigureAwait(false);

            return result.Map(_ =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                var message = $"Successfully created directory: {request.Path}";
                return new CallToolResult(
                [
                    new ContentItem("text", message)
                ],
                StructuredContent: new ToolOperationResultDto(Name, Success: true, message, Path: request.Path));
            });
        }
    }
}
