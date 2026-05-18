using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ILogger
    {
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(Exception ex, string message, params object[] args);
        void LogDebug(string message, params object[] args);
        void LogTrace(string message, params object[] args);
    }
}