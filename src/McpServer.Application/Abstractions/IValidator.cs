using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IValidator<T>
    {
        ValueTask<Fin<Unit>> ValidateAsync(T value, CancellationToken ct);
    }
}