using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;
using LanguageExt;

namespace McpServer.Application.Abstractions.Execution
{
    public interface IProcessExecutionService
    {
        ValueTask<Fin<ProcessExecutionResult>> RunAsync(RunProcessCommand command, CancellationToken ct);
    }
}