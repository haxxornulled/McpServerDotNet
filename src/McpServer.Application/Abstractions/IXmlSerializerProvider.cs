using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IXmlSerializerProvider
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string xml);
    }
}