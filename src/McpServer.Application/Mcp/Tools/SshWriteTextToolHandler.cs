using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class SshWriteTextToolHandler(
        ISshService sshService,
        ILogger<SshWriteTextToolHandler> logger) : IToolHandler<SshWriteTextRequest>
    {
        public string Name => "ssh.write_text";
        public string Description => "Writes text to a file on an SSH server.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    profile = new { type = "string" },
                    path = new { type = "string" },
                    content = new { type = "string" }
                },
                required = new[] { "profile", "path", "content" }
            });

        public IReadOnlyList<string> Validate(SshWriteTextRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Profile, "profile");
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Path, "path");
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(SshWriteTextRequest request, CancellationToken ct)
        {
            var result = await sshService
                .WriteTextAsync(new WriteSshTextCommand(
                    request.Profile,
                    request.Path,
                    request.Content), ct)
                .ConfigureAwait(false);

            return result.Map(sshResult =>
            {
                logger.LogInformation("Tool {ToolName} completed", Name);
                
                var content = JsonSerializer.Serialize(sshResult, new JsonSerializerOptions { WriteIndented = true });
                
                return new CallToolResult(
                [
                    new ContentItem("text", content)
                ], 
                StructuredContent: sshResult);
            });
        }
    }
}
