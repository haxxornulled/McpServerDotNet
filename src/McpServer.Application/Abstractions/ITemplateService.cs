using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITemplateService
    {
        ValueTask<Fin<string>> RenderAsync(string templateName, object model, CancellationToken ct);
    }
}