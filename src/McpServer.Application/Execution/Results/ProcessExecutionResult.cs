namespace McpServer.Application.Execution.Results
{
    public sealed record ProcessExecutionResult(
        string Command,
        IEnumerable<string>? Arguments,
        string WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut,
        bool OutputTruncated);
}
