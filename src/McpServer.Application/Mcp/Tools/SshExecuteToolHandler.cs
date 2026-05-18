using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class SshExecuteToolHandler(
        ISshService sshService,
        ILogger<SshExecuteToolHandler> logger) : IToolHandler<SshExecuteRequest>
    {
        public string Name => "ssh.execute";
        public string Description => "Executes a command on an SSH server.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    profile = new { type = "string" },
                    command = new { type = "string" },
                    working_directory = new { type = "string" },
                    args = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Optional command arguments. Prefer this over embedding arguments directly in the command string."
                    }
                },
                required = new[] { "profile", "command" }
            });

        public IReadOnlyList<string> Validate(SshExecuteRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Profile, "profile");
            if (request.Args is { Length: > 0 })
            {
                ToolRequestValidation.RequireExecutableOnly(errors, request.Command, "command");
                ToolRequestValidation.RequireSafeArgumentValues(errors, request.Args, "args");
            }
            else
            {
                ToolRequestValidation.RequireShellSafeCommandText(errors, request.Command, "command");
            }
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(SshExecuteRequest request, CancellationToken ct)
        {
            var validationErrors = Validate(request);
            if (validationErrors.Count > 0)
            {
                return LanguageExt.Common.Error.New(string.Join(" ", validationErrors));
            }

            var result = await sshService
                .ExecuteAsync(new ExecuteSshCommand(
                    request.Profile,
                    request.Command,
                    request.WorkingDirectory,
                    request.Args), ct)
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
