using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISerializer
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }
}