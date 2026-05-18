using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface IEmailTemplateProvider
    {
        ValueTask<Fin<string>> RenderAsync(string templateName, object model, CancellationToken ct);
    }
}