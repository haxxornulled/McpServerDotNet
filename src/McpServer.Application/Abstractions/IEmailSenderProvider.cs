using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEmailSenderProvider
    {
        ValueTask<Fin<Unit>> SendEmailAsync(string to, string subject, string body, CancellationToken ct);
    }
}