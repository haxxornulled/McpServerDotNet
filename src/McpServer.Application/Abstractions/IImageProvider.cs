using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IImageProvider
    {
        ValueTask<Fin<Unit>> ResizeAsync(string imagePath, int width, int height, CancellationToken ct);
        ValueTask<Fin<Unit>> ConvertAsync(string imagePath, string format, CancellationToken ct);
    }
}