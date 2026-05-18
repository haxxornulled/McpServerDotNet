using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IBlobStorageService
    {
        ValueTask<Fin<Unit>> UploadAsync(string container, string blobName, Stream content, CancellationToken ct);
        ValueTask<Fin<Stream>> DownloadAsync(string container, string blobName, CancellationToken ct);
        ValueTask<Fin<Unit>> DeleteAsync(string container, string blobName, CancellationToken ct);
    }
}