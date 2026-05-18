namespace McpServer.Application.Ssh.Results
{
    public record SshFileWriteResult(
        string Profile,
        string Host,
        int Port,
        string Username,
        string Path,
        long BytesWritten,
        bool Success);
}