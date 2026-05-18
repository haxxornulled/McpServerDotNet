using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEmailTemplateService
    {
        ValueTask<Fin<string>> RenderAsync(string templateName, object model, CancellationToken ct);
    }
}