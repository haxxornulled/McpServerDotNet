using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IJsonSerializerProvider
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json);
    }
}