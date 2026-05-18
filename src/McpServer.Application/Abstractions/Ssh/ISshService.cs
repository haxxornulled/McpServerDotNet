using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;
using LanguageExt;

namespace McpServer.Application.Abstractions.Ssh
{
    public interface ISshService
    {
        ValueTask<Fin<SshCommandResult>> ExecuteAsync(ExecuteSshCommand command, CancellationToken ct);
        ValueTask<Fin<SshFileWriteResult>> WriteTextAsync(WriteSshTextCommand command, CancellationToken ct);
    }
}