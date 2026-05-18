using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IClusterProvider
    {
        ValueTask<Fin<string>> GetLeaderAsync(CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<string>>> GetMembersAsync(CancellationToken ct);
    }
}