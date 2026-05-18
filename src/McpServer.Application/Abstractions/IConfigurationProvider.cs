using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IConfigurationProvider
    {
        string GetConnectionString(string name);
        T GetSetting<T>(string key, T defaultValue = default!);
        bool IsDevelopment();
        bool IsProduction();
        bool IsStaging();
    }
}