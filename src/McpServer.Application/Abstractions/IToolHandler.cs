using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IToolHandler<TRequest>
    {
        ValueTask<Fin<Unit>> HandleAsync(TRequest request, CancellationToken ct);
    }
}