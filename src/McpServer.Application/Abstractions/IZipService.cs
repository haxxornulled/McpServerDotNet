using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IZipService
    {
        ValueTask<Fin<Unit>> CreateAsync(string zipPath, IReadOnlyList<string> files, CancellationToken ct);
        ValueTask<Fin<Unit>> ExtractAsync(string zipPath, string extractPath, CancellationToken ct);
    }
}