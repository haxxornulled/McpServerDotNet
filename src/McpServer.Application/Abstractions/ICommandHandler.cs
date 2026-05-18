using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICommandHandler<TCommand, TResult>
    {
        ValueTask<Fin<TResult>> HandleAsync(TCommand command, CancellationToken ct);
    }
}