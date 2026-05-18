using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IStorageProvider
    {
        ValueTask<Fin<Unit>> UploadAsync(string fileName, Stream content, CancellationToken ct);
        ValueTask<Fin<Stream>> DownloadAsync(string fileName, CancellationToken ct);
        ValueTask<Fin<Unit>> DeleteAsync(string fileName, CancellationToken ct);
    }
}