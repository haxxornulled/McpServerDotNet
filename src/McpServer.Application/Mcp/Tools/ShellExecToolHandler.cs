using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Execution;
using McpServer.Application.Execution.Commands;
using LanguageExt.Common;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Mcp.Validation;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools
{
    public sealed class ShellExecToolHandler(
        IProcessExecutionService processExecutionService,
        ILogger<ShellExecToolHandler> logger,
        IShellExecutionPolicy shellExecutionPolicy) : IToolHandler<ShellExecRequest>
    {
        public string Name => "shell.exec";
        public string Description => "Executes an explicitly allowlisted command in the active project. Prefer executable plus args; bare shell command lines require local policy opt-in.";

        public JsonElement GetInputSchema() =>
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    command = new
                    {
                        type = "string",
                        description = "Executable path or a shell command line when args are omitted."
                    },
                    args = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "Pass arguments separately instead of embedding them in command."
                    },
                    workingDirectory = new
                    {
                        type = "string",
                        description = "Optional project-relative working directory."
                    },
                    timeoutSeconds = new { type = "integer", @default = 30, minimum = 1, maximum = 300 },
                    maxOutputChars = new { type = "integer", @default = 12000, minimum = 256, maximum = 200000 }
                },
                required = new[] { "command" }
            });

        public IReadOnlyList<string> Validate(ShellExecRequest request)
        {
            var errors = new List<string>();
            ToolRequestValidation.RequireRange(errors, request.TimeoutSeconds, "timeoutSeconds", 1, 300);
            ToolRequestValidation.RequireRange(errors, request.MaxOutputChars, "maxOutputChars", 256, 200000);
            ValidateCommandShape(errors, request);
            return errors;
        }

        public async ValueTask<Fin<CallToolResult>> Handle(ShellExecRequest request, CancellationToken ct)
        {
            var validationErrors = Validate(request);
            if (validationErrors.Count > 0)
            {
                return Error.New(string.Join(" ", validationErrors));
            }

            var requiresShellFallback = request.Args is not { Length: > 0 } && ShouldUseShellFallback(request.Command);
            var policyResult = shellExecutionPolicy.Validate(request, requiresShellFallback);
            if (policyResult.IsFail)
            {
                return PropagateFailure<CallToolResult>(policyResult);
            }

            var command = BuildExecutionCommand(request);
            var result = await processExecutionService
                .RunAsync(new RunProcessCommand(
                    command.Command,
                    command.Arguments,
                    request.WorkingDirectory,
                    request.TimeoutSeconds,
                    request.MaxOutputChars), ct)
                .ConfigureAwait(false);

            return result.Map(processResult =>
            {
                logger.LogInformation("Tool {ToolName} completed for command: {Command}", Name, request.Command);
                
                var text = $"Command: {processResult.Command}\n" +
                           $"Exit Code: {processResult.ExitCode}\n" +
                           $"Output: {processResult.StandardOutput}\n" +
                           $"Error: {processResult.StandardError}";

                return new CallToolResult(
                [
                    new ContentItem("text", text)
                ], 
                StructuredContent: processResult);
            });
        }


        private static Fin<T> PropagateFailure<T>(Fin<Unit> failure) =>
            failure.Match<Fin<T>>(
                Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
                Fail: error => error);

        private static void ValidateCommandShape(List<string> errors, ShellExecRequest request)
        {
            if (request.Args is { Length: > 0 })
            {
                ToolRequestValidation.RequireExecutableOnly(errors, request.Command, "command");
                ToolRequestValidation.RequireSafeArgumentValues(errors, request.Args, "args");
                return;
            }

            if (ShouldUseShellFallback(request.Command))
            {
                ToolRequestValidation.RequireShellSafeCommandText(errors, request.Command, "command");
                return;
            }

            ToolRequestValidation.RequireNonWhiteSpace(errors, request.Command, "command");
        }

        private static RunProcessCommand BuildExecutionCommand(ShellExecRequest request)
        {
            if (request.Args is { Length: > 0 } || !ShouldUseShellFallback(request.Command))
            {
                return new RunProcessCommand(request.Command, request.Args, request.WorkingDirectory, request.TimeoutSeconds, request.MaxOutputChars);
            }

            return OperatingSystem.IsWindows()
                ? new RunProcessCommand(
                    "cmd.exe",
                    [
                        "/c",
                        request.Command
                    ],
                    request.WorkingDirectory,
                    request.TimeoutSeconds,
                    request.MaxOutputChars)
                : new RunProcessCommand(
                    "/bin/sh",
                    [
                        "-lc",
                        request.Command
                    ],
                    request.WorkingDirectory,
                    request.TimeoutSeconds,
                    request.MaxOutputChars);
        }

        private static bool ShouldUseShellFallback(string command) =>
            LooksLikeShellLine(command) || LooksLikeWindowsShellBuiltin(command);

        private static string ExtractExecutableName(string command)
        {
            var first = SplitBareCommandArguments(command).FirstOrDefault() ?? command;
            return Path.GetFileNameWithoutExtension(first.Trim()).ToLowerInvariant();
        }

        private static IReadOnlyList<string> SplitBareCommandArguments(string command) =>
            command.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static bool LooksLikeShellLine(string command) =>
            command.Contains(' ') || command.Contains('\t') || command.Contains('|') || command.Contains('&') || command.Contains(';');

        private static bool LooksLikeWindowsShellBuiltin(string command) =>
            OperatingSystem.IsWindows() &&
            WindowsShellBuiltins.Contains(ExtractExecutableName(command));

        private static readonly System.Collections.Generic.HashSet<string> WindowsShellBuiltins = new(StringComparer.OrdinalIgnoreCase)
        {
            "cd",
            "cls",
            "copy",
            "del",
            "dir",
            "echo",
            "erase",
            "md",
            "mkdir",
            "move",
            "popd",
            "pushd",
            "rd",
            "ren",
            "rename",
            "rmdir",
            "type"
        };
    }
}
