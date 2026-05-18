using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IXmlSerializer
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string xml);
    }
}