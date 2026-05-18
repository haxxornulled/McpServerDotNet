using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICsvSerializerProvider
    {
        string Serialize<T>(IReadOnlyList<T> records);
        IReadOnlyList<T> Deserialize<T>(string csv);
    }
}