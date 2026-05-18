using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICommandBusProvider
    {
        ValueTask<Fin<Unit>> SendAsync<T>(T command, CancellationToken ct) where T : class;
    }
}