using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IWorkflowProvider
    {
        ValueTask<Fin<Unit>> StartAsync(string workflowId, IDictionary<string, object> parameters, CancellationToken ct);
        ValueTask<Fin<Unit>> CancelAsync(string workflowId, CancellationToken ct);
    }
}