using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IRuleEngineProvider
    {
        ValueTask<Fin<bool>> EvaluateAsync(string rule, IDictionary<string, object> context, CancellationToken ct);
    }
}