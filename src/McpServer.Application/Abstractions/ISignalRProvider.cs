using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISignalRProvider
    {
        ValueTask<Fin<Unit>> SendAsync<T>(string hub, string method, T data, CancellationToken ct);
    }
}