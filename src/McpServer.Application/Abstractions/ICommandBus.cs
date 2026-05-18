using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICommandBus
    {
        ValueTask<Fin<Unit>> SendAsync<T>(T command, CancellationToken ct) where T : class;
    }
}