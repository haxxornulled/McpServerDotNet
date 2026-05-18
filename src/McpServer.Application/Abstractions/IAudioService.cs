using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IAudioService
    {
        ValueTask<Fin<Unit>> ConvertAsync(string audioPath, string format, CancellationToken ct);
        ValueTask<Fin<Unit>> NormalizeAsync(string audioPath, CancellationToken ct);
    }
}