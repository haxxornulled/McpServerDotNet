namespace McpServer.Application.Ssh.Commands;

public sealed record WriteSshTextCommand(
    string Profile,
    string Path,
    string Content,
    bool Overwrite = true,
    bool CreateDirectories = true,
    string Encoding = "utf-8",
    string? Permissions = null);
