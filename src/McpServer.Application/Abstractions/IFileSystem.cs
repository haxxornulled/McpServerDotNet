using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IFileSystem
    {
        ValueTask<Fin<Unit>> CreateDirectoryAsync(string path, CancellationToken ct);
        ValueTask<Fin<Unit>> DeleteAsync(string path, CancellationToken ct);
        ValueTask<Fin<bool>> ExistsAsync(string path, CancellationToken ct);
        ValueTask<Fin<Unit>> MoveAsync(string sourcePath, string destinationPath, CancellationToken ct);
        ValueTask<Fin<Unit>> CopyAsync(string sourcePath, string destinationPath, CancellationToken ct);
    }
}