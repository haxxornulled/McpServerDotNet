using LanguageExt;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Execution
{
    public class ProcessExecutionService : IProcessExecutionService
    {
        private readonly ILogger<ProcessExecutionService> _logger;

        public ProcessExecutionService(ILogger<ProcessExecutionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ValueTask<Fin<ProcessExecutionResult>> RunAsync(RunProcessCommand command, CancellationToken ct)
        {
            // Hot path optimization - check for null/empty immediately
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                _logger.LogWarning("RunAsync called with invalid command");
                return new ValueTask<Fin<ProcessExecutionResult>>(Fin<ProcessExecutionResult>.Fail(Error.New("Command cannot be null or empty")));
            }

            try
            {
                _logger.LogInformation("Running process: {Command}", command.Command);

                // In a real implementation, this would execute an actual process
                // For now, we'll simulate with mock behavior
                var output = $"Output from command: {command.Command}";
                var errorOutput = string.Empty;
                var exitCode = 0;
                var timedOut = false;
                var truncated = false;

                // Simulate some processing time for demonstration purposes
                // Note: We're not awaiting this since we're returning ValueTask
                // In a real implementation, this would be actual process execution

                // For demonstration purposes, we'll return different outputs based on command
                if (command.Command.Contains("echo", StringComparison.OrdinalIgnoreCase))
                {
                    output = "Hello, World!";
                }
                else if (command.Command.Contains("dir", StringComparison.OrdinalIgnoreCase) || 
                         command.Command.Contains("ls", StringComparison.OrdinalIgnoreCase))
                {
                    output = "File1.txt\nFile2.txt\ndirectory1/";
                }
                else if (command.Command.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    errorOutput = "This is a simulated error message";
                    exitCode = 1;
                }

                var result = new ProcessExecutionResult(
                    command.Command,
                    command.Arguments,
                    command.WorkingDirectory ?? string.Empty,
                    exitCode,
                    output,
                    errorOutput,
                    timedOut,
                    truncated);

                _logger.LogInformation("Successfully ran process: {Command}", command.Command);
                
                return new ValueTask<Fin<ProcessExecutionResult>>(Fin<ProcessExecutionResult>.Succ(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to run process: {Command}", command.Command);
                return new ValueTask<Fin<ProcessExecutionResult>>(Fin<ProcessExecutionResult>.Fail(Error.New($"Failed to run process: {ex.Message}")));
            }
        }
    }
}

