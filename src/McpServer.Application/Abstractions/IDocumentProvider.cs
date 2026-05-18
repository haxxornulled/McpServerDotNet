using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IDocumentProvider
    {
        ValueTask<Fin<Unit>> ConvertAsync(string documentPath, string format, CancellationToken ct);
        ValueTask<Fin<Unit>> ExtractTextAsync(string documentPath, CancellationToken ct);
    }
}