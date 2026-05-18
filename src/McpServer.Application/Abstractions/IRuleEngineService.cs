using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IRuleEngineService
    {
        ValueTask<Fin<bool>> EvaluateAsync(string rule, IDictionary<string, object> context, CancellationToken ct);
    }
}