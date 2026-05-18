using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEmailService
    {
        ValueTask<Fin<Unit>> SendEmailAsync(string to, string subject, string body, CancellationToken ct);
    }
}