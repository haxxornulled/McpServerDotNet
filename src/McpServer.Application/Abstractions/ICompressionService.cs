using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICompressionService
    {
        ValueTask<Fin<Stream>> CompressAsync(Stream input, CancellationToken ct);
        ValueTask<Fin<Stream>> DecompressAsync(Stream input, CancellationToken ct);
    }
}