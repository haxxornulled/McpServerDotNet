using LanguageExt;
using McpServer.Application.Abstractions;
using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;

namespace McpServer.Application.Execution
{
    public interface IProcessService
    {
        ValueTask<Fin<ProcessExecutionResult>> RunAsync(RunProcessCommand command, CancellationToken ct);
    }
}