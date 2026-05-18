using LanguageExt;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;

namespace McpServer.Application.Abstractions
{
    public interface ISshService
    {
        ValueTask<Fin<SshCommandResult>> ExecuteAsync(ExecuteSshCommand command, CancellationToken ct);
        ValueTask<Fin<SshFileWriteResult>> WriteTextAsync(WriteSshTextCommand command, CancellationToken ct);
    }
}