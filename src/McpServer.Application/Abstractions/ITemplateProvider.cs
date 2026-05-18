using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ITemplateProvider
    {
        ValueTask<Fin<string>> RenderAsync(string templateName, object model, CancellationToken ct);
    }
}