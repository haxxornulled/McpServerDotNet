using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITokenGeneratorProvider
    {
        string GenerateToken(string userId, string role);
    }
}