using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICommandService
    {
        ValueTask<Fin<Unit>> ExecuteAsync(string command, IDictionary<string, object> parameters, CancellationToken ct);
    }
}