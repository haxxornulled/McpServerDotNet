using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class FsDeletePathToolHandler(
        IFileSystemService fileSystemService,
        ILogger<FsDeletePathToolHandler> logger) : IToolHandler<DeletePathRequest>
    {
        public string Name => "fs.delete_path";
        public string Description => "Deletes a file or directory.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    path = new { type = "string" },
                    recursive = new { type = "boolean" },
                    confirmation = new { type = "string", description = "Required for recursive directory delete. Must equal DELETE plus the normalized absolute target path returned in the policy error." }
                },
                required = new[] { "path" }
            });

        public IReadOnlyList<string> Validate(DeletePathRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            ToolRequestValidation.RequireNotRootLikePath(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(DeletePathRequest request, CancellationToken ct)
        {
            var result = await fileSystemService
                .DeletePathAsync(new DeletePathCommand(request.Path, request.Recursive, request.Confirmation), ct)
                .ConfigureAwait(false);

            return result.Map(_ =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                var message = $"Successfully deleted path: {request.Path}";
                return new CallToolResult(
                [
                    new ContentItem("text", message)
                ],
                StructuredContent: new ToolOperationResultDto(Name, Success: true, message, Path: request.Path));
            });
        }
    }
}
