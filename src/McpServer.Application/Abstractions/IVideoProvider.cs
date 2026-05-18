using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IVideoProvider
    {
        ValueTask<Fin<Unit>> ConvertAsync(string videoPath, string format, CancellationToken ct);
        ValueTask<Fin<Unit>> CompressAsync(string videoPath, int quality, CancellationToken ct);
    }
}