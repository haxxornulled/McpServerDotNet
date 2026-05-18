using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICommandProvider
    {
        ValueTask<Fin<Unit>> ExecuteAsync(string command, IDictionary<string, object> parameters, CancellationToken ct);
    }
}