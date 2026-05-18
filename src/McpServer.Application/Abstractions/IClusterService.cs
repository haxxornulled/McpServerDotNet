using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IClusterService
    {
        ValueTask<Fin<string>> GetLeaderAsync(CancellationToken ct);
        ValueTask<Fin<IReadOnlyList<string>>> GetMembersAsync(CancellationToken ct);
    }
}