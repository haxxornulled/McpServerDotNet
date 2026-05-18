using LanguageExt;
using McpServer.Application.Abstractions;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;

namespace McpServer.Application.Ssh
{
    public interface ISshService
    {
        ValueTask<Fin<SshCommandResult>> ExecuteAsync(ExecuteSshCommand command, CancellationToken ct);
        ValueTask<Fin<SshFileWriteResult>> WriteTextAsync(WriteSshTextCommand command, CancellationToken ct);
    }
}