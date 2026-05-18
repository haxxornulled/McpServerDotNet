using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ISerializerProvider
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }
}