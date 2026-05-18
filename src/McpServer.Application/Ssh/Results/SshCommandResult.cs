namespace McpServer.Application.Ssh.Results
{
    public record SshCommandResult(
        string Profile,
        string Host,
        int Port,
        string Username,
        string Command,
        string? WorkingDirectory,
        int ExitCode,
        string StandardOutput,
        string StandardError,
        bool TimedOut,
        bool OutputTruncated);
}
