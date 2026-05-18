using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEmailSender
    {
        ValueTask<Fin<Unit>> SendEmailAsync(string to, string subject, string body, CancellationToken ct);
    }
}