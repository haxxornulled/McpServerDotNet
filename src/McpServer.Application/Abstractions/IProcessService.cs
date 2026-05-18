using LanguageExt;
using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;

namespace McpServer.Application.Abstractions
{
    public interface IProcessService
    {
        ValueTask<Fin<ProcessExecutionResult>> RunAsync(RunProcessCommand command, CancellationToken ct);
    }
}