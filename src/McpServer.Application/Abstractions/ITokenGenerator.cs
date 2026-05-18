using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITokenGenerator
    {
        string GenerateToken(string userId, string role);
    }
}